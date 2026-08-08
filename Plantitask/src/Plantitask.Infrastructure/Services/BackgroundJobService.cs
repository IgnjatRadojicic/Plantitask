using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// Thin wrapper around Hangfire scheduling so services depend on an interface instead of
    /// static Hangfire calls. Owns the recurring job registrations and the one-off due-soon
    /// reminders.
    /// </summary>
    public class BackgroundJobService : IBackgroundJobService
    {
        private readonly ILogger<BackgroundJobService> _logger;
        private readonly INotificationService _notificationService;

        public BackgroundJobService(ILogger<BackgroundJobService> logger, INotificationService notificationService)
        {
            _logger = logger;
            _notificationService = notificationService;
        }

        /// <summary>
        /// Deletes a scheduled job by id, quietly accepting null or empty so callers can pass
        /// whatever DueSoonJobId happens to hold.
        /// </summary>
        public void CancelScheduledJob(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return;

            BackgroundJob.Delete(jobId);
            _logger.LogInformation("Cancelled scheduled job {JobId}", jobId);
        }

        /// <summary>
        /// Schedules the due-soon reminder at the assignee's preferred offset before the due
        /// date. Returns the Hangfire JobId for the task row to keep (that is what makes
        /// cancel-on-change possible), or null when the reminder time is already in the past.
        /// </summary>
        public async Task<string?> ScheduleTaskDueSoonNotification(Guid taskId, Guid userId, DateTime dueDate)
        {
            int hours = await _notificationService.GetReminderHoursBeforeAsync(userId);

            if (dueDate.Kind != DateTimeKind.Utc)
            {
                dueDate = DateTime.SpecifyKind(dueDate, DateTimeKind.Utc);
            }
            var reminderTime = dueDate.AddHours(-hours);



            if (reminderTime <= DateTime.UtcNow)
            {
                _logger.LogWarning("Cannot schedule due soon notification for task {TaskId} - reminder time {ReminderTime} is in the past",
                    taskId, reminderTime);
                return null;
            }

            var jobId = BackgroundJob.Schedule<NotificationBackgroundJob>(
                job => job.SendTaskDueSoonNotification(taskId),
                reminderTime);

            _logger.LogInformation("Scheduled due soon notification for task {TaskId} at {ReminderTime} (JobId: {JobId})",
             taskId, reminderTime, jobId);

            return jobId;
        }

        /// <summary>
        /// Registers the three recurring jobs at startup: overdue check (daily 00:00 UTC),
        /// one-time premium expiry (daily 01:00 UTC) and notification cleanup (weekly Sunday
        /// 02:00 UTC). AddOrUpdate makes this idempotent across restarts.
        /// </summary>
        public void SetupRecurringJobs()
        {
            RecurringJob.AddOrUpdate<NotificationBackgroundJob>(
                "check-overdue-tasks",
                job => job.CheckOverdueTasksAndNotify(),
                Cron.Daily(hour: 0, minute: 0));

            RecurringJob.AddOrUpdate<NotificationBackgroundJob>(
            "cleanup-old-notifications",
            job => job.CleanupOldNotifications(),
            Cron.Weekly(DayOfWeek.Sunday, hour: 2, minute: 0));

            RecurringJob.AddOrUpdate<PremiumBackgroundJob>(
                "expire-onetime-premium",
                job => job.ExpireOneTimePremiumAsync(),
                Cron.Daily(hour: 1, minute: 0));

            _logger.LogInformation(
                "Recurring jobs configured: check-overdue-tasks (daily 00:00 UTC), expire-onetime-premium (daily 01:00 UTC), cleanup-old-notifications (weekly Sun 02:00 UTC)");
        }

        /// <summary>Runs the overdue digest immediately instead of waiting for midnight - a dev and support lever.</summary>
        public void TriggerOverdueCheck()
        {
            BackgroundJob.Enqueue<NotificationBackgroundJob>(job => job.CheckOverdueTasksAndNotify());
            _logger.LogInformation("Manually triggered overdue check");


        }
    }
}