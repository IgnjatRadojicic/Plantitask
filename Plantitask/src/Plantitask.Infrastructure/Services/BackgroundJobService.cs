using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    public class BackgroundJobService : IBackgroundJobService
    {
        private readonly ILogger<BackgroundJobService> _logger;
        private readonly INotificationService _notificationService;

        public BackgroundJobService(ILogger<BackgroundJobService> logger, INotificationService notificationService)
        {
            _logger = logger;
            _notificationService = notificationService;
        }

        public void CancelScheduledJob(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return;

            BackgroundJob.Delete(jobId);
            _logger.LogInformation("Cancelled scheduled job {JobId}", jobId);
        }

        public async Task<string> ScheduleTaskDueSoonNotification(Guid taskId, Guid userId, DateTime dueDate)
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
                return string.Empty;
            }

            var jobId = BackgroundJob.Schedule<NotificationBackgroundJob>(
                job => job.SendTaskDueSoonNotification(taskId),
                reminderTime);

            _logger.LogInformation("Scheduled due soon notification for task {TaskId} at {ReminderTime} (JobId: {JobId})",
             taskId, reminderTime, jobId);

            return jobId;
        }

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

            _logger.LogInformation("Recurring jobs configured: check-overdue-tasks (daily), cleanup-old-notifications (weekly)");
        }

        public void TriggerOverdueCheck()
        {
            BackgroundJob.Enqueue<NotificationBackgroundJob>(job => job.CheckOverdueTasksAndNotify());
            _logger.LogInformation("Manually triggered overdue check");


        }
    }
}