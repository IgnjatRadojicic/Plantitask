using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using System.Linq;

namespace Plantitask.Infrastructure.Services
{
    public class NotificationBackgroundJob
    {
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

        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 3600 })]
        public async Task SendTaskDueSoonNotification(Guid taskId)
        {
            _logger.LogInformation("Processing due soon notification for task {TaskId}", taskId);

            var task = await _context.Tasks
                .Include(t => t.AssignedTo)
                .Include(t => t.Status)
                .FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted);

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

            var existingNotification = await _context.Notifications
                .Where(n => n.RelatedEntityId == task.Id
                && n.Type == NotificationType.TaskDueSoon
                && !n.IsDeleted)
                .AnyAsync();

            if (existingNotification)
            {
                _logger.LogInformation("Due soon notification already exists for task {TaskId}", taskId);
                return;
            }


            var notification = new Notification
            {
                UserId = task.AssignedToId.Value,
                Type = NotificationType.TaskDueSoon,
                Title = "Task Due Soon",
                Message = $"Task '{task.Title}' is due in 24 hours",
                RelatedEntityId = task.Id,
                RelatedEntityType = "Task",
                CreatedBy = task.AssignedToId.Value
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();


            try
            {
                if (await _notificationService.ShouldEmailAsync(task.AssignedToId.Value, NotificationType.TaskDueSoon))
                {
                    await _emailService.SendTaskDueSoonEmailAsync(
                        task.AssignedTo!.Email,
                        task.AssignedTo.UserName,
                        task.Title,
                        task.DueDate!.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send due soon email for task {TaskId}", taskId);
            }

            _logger.LogInformation("Due soon notification created for task {TaskId}", taskId);
        }


        [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 3600 })]
        public async Task CheckOverdueTasksAndNotify()
        {
            _logger.LogInformation("Starting overdue tasks check");

            var now = DateTime.UtcNow;

            var overdueTasks = await _context.Tasks
                .Include(t => t.AssignedTo)
                .Where(t => !t.IsDeleted
                 && t.StatusId != (int)TaskStatusItem.Completed
                 && t.DueDate.HasValue
                 && t.DueDate.Value < now
                 && t.AssignedToId != null)
                .ToListAsync();
            _logger.LogInformation("Found {Count} overdue tasks", overdueTasks.Count);

            if (overdueTasks.Count == 0)
                return;

            var overdueTaskIds = overdueTasks.Select(t => t.Id).ToList();

            var alreadyNotifiedTaskIds = (await _context.Notifications
            .Where(n => overdueTaskIds.Contains(n.RelatedEntityId!.Value)
                && n.Type == NotificationType.TaskOverdue
                && n.CreatedAt >= now.Date
                && !n.IsDeleted)
            .Select(n => n.RelatedEntityId!.Value)
            .ToListAsync()
            ).ToHashSet();

            var notificationsCreated = 0;

            foreach (var task in overdueTasks)
            {
                if (alreadyNotifiedTaskIds.Contains(task.Id))
                {
                    _logger.LogDebug("Overdue notification already sent today for task {TaskId}", task.Id);
                    continue;
                }

                var daysSinceOverdue = (now - task.DueDate!.Value).Days;

                var notification = new Notification
                {
                    UserId = task.AssignedToId!.Value,
                    Type = NotificationType.TaskOverdue,
                    Title = "Task Overdue",
                    Message = daysSinceOverdue == 0
                        ? $"Task '{task.Title}' is overdue"
                        : $"Task '{task.Title}' is overdue by {daysSinceOverdue} day(s)",
                    RelatedEntityId = task.Id,
                    RelatedEntityType = "Task",
                    CreatedBy = task.AssignedToId.Value
                };

                _context.Notifications.Add(notification);
                notificationsCreated++;

                try
                {
                    if (await _notificationService.ShouldEmailAsync(task.AssignedToId!.Value, NotificationType.TaskOverdue))
                    {
                        await _emailService.SendTaskOverdueEmailAsync(
                            task.AssignedTo!.Email,
                            task.AssignedTo.UserName,
                            task.Title,
                            daysSinceOverdue);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send overdue email for task {TaskId}", task.Id);
                }
            }

            if (notificationsCreated > 0)
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Created {Count} overdue notifications", notificationsCreated);
            }
            else
            {
                _logger.LogInformation("No new overdue notifications needed");
            }
        }


            [AutomaticRetry(Attempts = 2)]
        public async Task CleanupOldNotifications()
        {
            _logger.LogInformation("Starting notification cleanup");

            int daysofreadnotfications = 30;

            var cutoffDate = DateTime.UtcNow.AddDays(-daysofreadnotfications);

            var deletedCount = await _context.Notifications
                .Where(n => n.IsRead
                    && n.ReadAt.HasValue
                    && n.ReadAt.Value < cutoffDate
                    && !n.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(n => n.IsDeleted, true)
                    .SetProperty(n => n.DeletedAt, DateTime.UtcNow));

            _logger.LogInformation("Found {Count} old notifications to soft delete", deletedCount);

        }
    }
}

