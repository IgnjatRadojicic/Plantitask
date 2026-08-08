using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Projections;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// The Hangfire side of notifications: scheduled due-soon reminders, the daily overdue
    /// digest and retention cleanup. Every job here is written to be retried - Hangfire will
    /// re-run them on failure, so each one has to be safe to run twice.
    /// </summary>
    public class NotificationBackgroundJob
    {
        private const int WorstTasksInDigestEmail = 2;
        private const int ReadNotificationRetentionDays = 30;
        private const int DigestLogRetentionDays = 30;

        private readonly IApplicationDbContext _context;
        private readonly ILogger<NotificationBackgroundJob> _logger;
        private readonly IEmailService _emailService;
        private readonly INotificationService _notificationService;


        public NotificationBackgroundJob(IApplicationDbContext context,
            ILogger<NotificationBackgroundJob> logger,
            IEmailService emailService,
            INotificationService notificationService)
        {
            _context = context;
            _logger = logger;
            _emailService = emailService;
            _notificationService = notificationService;
        }

        /// <summary>
        /// Fires at the scheduled reminder time. Re-checks the world before sending - the task
        /// may have been completed, unassigned or deleted since scheduling - and treats each of
        /// those as a quiet skip, not an error. The notification commits first; the email is
        /// best effort after it.
        /// </summary>
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 3600 })]
        public async Task SendTaskDueSoonNotification(Guid taskId)
        {
            _logger.LogInformation("Processing due soon notification for task {TaskId}", taskId);

            var task = await _context.Tasks
                .Where(t => t.Id == taskId)
                .Select(TaskProjections.ToReminder)
                .FirstOrDefaultAsync();

            if (task == null)
            {
                _logger.LogWarning("Task {TaskId} not found for due soon notification", taskId);
                return;
            }

            if (task.StatusId == (int)TaskStatusItem.Completed)
            {
                _logger.LogInformation("Task {TaskId} is already completed, skipping notification", taskId);
                return;
            }

            if (!task.AssignedToId.HasValue)
            {
                _logger.LogInformation("Task {TaskId} has no assignee, skipping notification", taskId);
                return;
            }

            var userId = task.AssignedToId.Value;

            if (await _notificationService.ShouldNotifyAsync(userId, NotificationType.TaskDueSoon))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = userId,
                    Type = NotificationType.TaskDueSoon,
                    Title = "Task Due Soon",
                    Message = $"Task '{task.Title}' is due soon",
                    RelatedEntityId = task.Id,
                    RelatedEntityType = "Task",
                    RelatedDate = task.DueDate!.Value,
                });

                await _context.SaveChangesAsync();
                _logger.LogInformation("Due soon notification created for task {TaskId}", taskId);
            }

            try
            {
                if (await _notificationService.ShouldEmailAsync(userId, NotificationType.TaskDueSoon))
                {
                    await _emailService.SendTaskDueSoonEmailAsync(
                        task.AssigneeEmail!,
                        task.AssigneeName!,
                        task.Title,
                        task.DueDate!.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send due soon email for task {TaskId}", taskId);
            }
        }


        /// <summary>
        /// The daily overdue digest: one notification and one email per user, no matter how many
        /// tasks are overdue. The NotificationDigestLogs table is what makes reruns safe - a user
        /// already digested today is skipped, so a retry cannot double-send. Notifications and
        /// digest markers commit together; emails go out afterwards, each in its own try.
        /// </summary>
        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 3600 })]
        [DisableConcurrentExecution(timeoutInSeconds: 300)]
        public async Task CheckOverdueTasksAndNotify()
        {
            _logger.LogInformation("Starting overdue tasks check");

            var now = DateTime.UtcNow;

            var overdueTasks = await _context.Tasks
                .Where(t => t.StatusId != (int)TaskStatusItem.Completed
                 && t.DueDate.HasValue
                 && t.DueDate.Value < now
                 && t.AssignedToId != null)
                .Select(TaskProjections.ToReminder)
                .ToListAsync();
            _logger.LogInformation("Found {Count} overdue tasks", overdueTasks.Count);

            if (overdueTasks.Count == 0)
                return;

            var userIds = overdueTasks.Select(t => t.AssignedToId!.Value).Distinct().ToList();

            var today = DateOnly.FromDateTime(now);

            var alreadyDigestedUserIds = await _context.NotificationDigestLogs
                .Where(l => userIds.Contains(l.UserId)
                    && l.Type == NotificationType.TaskOverdue
                    && l.SentOn == today)
                .Select(l => l.UserId)
                .ToHashSetAsync();

            var preferences = await _context.NotificationPreferences
                .Where(np => userIds.Contains(np.UserId) && np.Type == NotificationType.TaskOverdue)
                .Select(np => new { np.UserId, np.IsEnabled, np.IsEmailEnabled })
                .ToListAsync();

            var inAppDisabled = preferences.Where(p => !p.IsEnabled).Select(p => p.UserId).ToHashSet();
            var emailDisabled = preferences.Where(p => !p.IsEmailEnabled).Select(p => p.UserId).ToHashSet();

            var digests = overdueTasks
                .GroupBy(t => t.AssignedToId!.Value)
                .Where(g => !alreadyDigestedUserIds.Contains(g.Key))
                .Select(g => new
                {
                    UserId = g.Key,
                    Email = g.First().AssigneeEmail,
                    UserName = g.First().AssigneeName,
                    Tasks = g.OrderBy(t => t.DueDate!.Value)
                             .Select(t => new OverdueTaskLine(t.Title, (now - t.DueDate!.Value).Days))
                             .ToList()
                })
                .ToList();

            if (digests.Count == 0)
            {
                _logger.LogInformation("Every user with overdue tasks was already digested today");
                return;
            }

            foreach (var digest in digests)
            {
                if (!inAppDisabled.Contains(digest.UserId))
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = digest.UserId,
                        Type = NotificationType.TaskOverdue,
                        Title = "Tasks Overdue",
                        Message = BuildDigestMessage(digest.Tasks),
                        RelatedEntityType = "Task",
                    });
                }

                _context.NotificationDigestLogs.Add(new NotificationDigestLog
                {
                    UserId = digest.UserId,
                    Type = NotificationType.TaskOverdue,
                    SentOn = today,
                });
            }

            await _context.SaveChangesAsync();

            foreach (var digest in digests)
            {
                if (emailDisabled.Contains(digest.UserId) || string.IsNullOrEmpty(digest.Email))
                    continue;

                try
                {
                    await _emailService.SendTaskOverdueDigestEmailAsync(
                        digest.Email,
                        digest.UserName ?? string.Empty,
                        digest.Tasks.Count,
                        digest.Tasks.Take(WorstTasksInDigestEmail).ToList());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send overdue digest email to user {UserId}", digest.UserId);
                }
            }

            _logger.LogInformation("Sent overdue digests to {Count} users", digests.Count);
        }

        /// <summary>
        /// One task gets a specific sentence with the day count; more than one collapses to a
        /// total, because the notification row has no room for a list.
        /// </summary>
        private static string BuildDigestMessage(IReadOnlyList<OverdueTaskLine> tasks)
        {
            if (tasks.Count == 1)
            {
                var only = tasks[0];

                if (only.DaysOverdue == 0)
                    return $"'{only.Title}' is overdue";

                return only.DaysOverdue == 1
                    ? $"'{only.Title}' is overdue by 1 day"
                    : $"'{only.Title}' is overdue by {only.DaysOverdue} days";
            }

            return $"You have {tasks.Count} overdue tasks";
        }


        /// <summary>
        /// Weekly retention: soft-deletes notifications read more than 30 days ago and hard
        /// deletes digest log rows past their window. Both are set-based statements, and the
        /// soft delete stamps UpdatedAt by hand since ExecuteUpdate bypasses the override.
        /// </summary>
        [AutomaticRetry(Attempts = 2)]
        public async Task CleanupOldNotifications()
        {
            _logger.LogInformation("Starting notification cleanup");

            var now = DateTime.UtcNow;
            var cutoffDate = now.AddDays(-ReadNotificationRetentionDays);

            var softDeletedCount = await _context.Notifications
                .Where(n => n.IsRead
                    && n.ReadAt.HasValue
                    && n.ReadAt.Value < cutoffDate)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(n => n.IsDeleted, true)
                    .SetProperty(n => n.DeletedAt, now)
                    .SetProperty(n => n.UpdatedAt, now));

            _logger.LogInformation("Soft-deleted {Count} read notifications older than {Days} days",
                softDeletedCount, ReadNotificationRetentionDays);

            var digestLogCutoff = DateOnly.FromDateTime(now.AddDays(-DigestLogRetentionDays));

            var prunedLogCount = await _context.NotificationDigestLogs
                .Where(l => l.SentOn < digestLogCutoff)
                .ExecuteDeleteAsync();

            _logger.LogInformation("Deleted {Count} digest log rows older than {Days} days",
                prunedLogCount, DigestLogRetentionDays);
        }
    }
}

