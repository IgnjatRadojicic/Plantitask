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
        private readonly IBackgroundJobClient _jobs;
        private readonly IRecurringJobManager _recurringJobs;
        private readonly ILogger<BackgroundJobService> _logger;
        private readonly INotificationService _notificationService;

        public BackgroundJobService(
            IBackgroundJobClient jobs,
            IRecurringJobManager recurringJobs,
            ILogger<BackgroundJobService> logger,
            INotificationService notificationService)
        {
            _jobs = jobs;
            _recurringJobs = recurringJobs;
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

            _jobs.Delete(jobId);
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

            var jobId = _jobs.Schedule<NotificationBackgroundJob>(
                job => job.SendTaskDueSoonNotification(taskId),
                reminderTime);

            _logger.LogInformation("Scheduled due soon notification for task {TaskId} at {ReminderTime} (JobId: {JobId})",
             taskId, reminderTime, jobId);

            return jobId;
        }

        /// <summary>
        /// Registers the three recurring jobs at startup: overdue check (daily 00:00 UTC),
        /// and notification cleanup (weekly Sunday 02:00 UTC). AddOrUpdate makes this idempotent
        /// across restarts.
        /// </summary>
        public void SetupRecurringJobs()
        {
            _recurringJobs.AddOrUpdate<NotificationBackgroundJob>(
                "check-overdue-tasks",
                job => job.CheckOverdueTasksAndNotify(),
                Cron.Daily(hour: 0, minute: 0));

            _recurringJobs.AddOrUpdate<NotificationBackgroundJob>(
            "cleanup-old-notifications",
            job => job.CleanupOldNotifications(),
            Cron.Weekly(DayOfWeek.Sunday, hour: 2, minute: 0));

            // expire-onetime-premium is gone. Premium now ends when a grant's EndsAt passes,
            // which needs nothing to run, so there is no longer a window between a pass expiring
            // and a job noticing during which the old limits were still being enforced.

            // Every fifteen minutes rather than nightly because this interval is the window in
            // which the storage quota disagrees with the disk. Deleting a tree frees quota at
            // once; the bytes go when this runs.
            _recurringJobs.AddOrUpdate<AttachmentPurgeJob>(
                "purge-deleted-attachment-files",
                job => job.PurgeDeletedAttachmentFilesAsync(),
                "*/15 * * * *");

            _logger.LogInformation(
                "Recurring jobs configured: check-overdue-tasks (daily 00:00 UTC), purge-deleted-attachment-files (every 15 min), cleanup-old-notifications (weekly Sun 02:00 UTC)");
        }

        /// <summary>Runs the overdue digest immediately instead of waiting for midnight - a dev and support lever.</summary>
        public void TriggerOverdueCheck()
        {
            _jobs.Enqueue<NotificationBackgroundJob>(job => job.CheckOverdueTasksAndNotify());
            _logger.LogInformation("Manually triggered overdue check");


        }
    }
}