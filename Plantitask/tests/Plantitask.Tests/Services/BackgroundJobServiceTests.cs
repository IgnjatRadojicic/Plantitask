using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;

namespace Plantitask.Tests.Services
{
    /// <summary>
    /// Schedule, Enqueue, Delete and the generic AddOrUpdate are extension methods, so Moq cannot
    /// see them. Each one funnels into a real interface member and that is where the setups and
    /// verifications have to sit.
    /// </summary>
    public class BackgroundJobServiceTests
    {
        private static readonly Guid TaskId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
        private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

        private readonly Mock<IBackgroundJobClient> _jobs = new();
        private readonly Mock<IRecurringJobManager> _recurringJobs = new();
        private readonly Mock<INotificationService> _notifications = new();
        private readonly BackgroundJobService _sut;

        public BackgroundJobServiceTests()
        {
            _jobs
                .Setup(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()))
                .Returns("hangfire-job-id");

            _notifications
                .Setup(n => n.GetReminderHoursBeforeAsync(It.IsAny<Guid>()))
                .ReturnsAsync(24);

            _sut = new BackgroundJobService(
                _jobs.Object,
                _recurringJobs.Object,
                NullLogger<BackgroundJobService>.Instance,
                _notifications.Object);

        }

        private (Job Job, IState State) SingleCreatedJob()
        {
            var call = Assert.Single(_jobs.Invocations.Where(i => i.Method.Name == nameof(IBackgroundJobClient.Create)));

            return ((Job)call.Arguments[0], (IState)call.Arguments[1]);
        }

        [Fact]
        public async Task ScheduleTaskDueSoonNotification_ReturnsTheIdHangfireGaveBack()
        {
            var jobId = await _sut.ScheduleTaskDueSoonNotification(
                TaskId, UserId, DateTime.UtcNow.AddDays(5));

            Assert.Equal("hangfire-job-id", jobId);
        }

        /// <summary>
        /// The reminder lands at the user's own preferred offset rather than a fixed one, so the
        /// preference is read per user and subtracted from the due date.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(24)]
        [InlineData(72)]
        public async Task ScheduleTaskDueSoonNotification_SchedulesTheUsersPreferredHoursBeforeTheDueDate(int hours)
        {
            _notifications.Setup(n => n.GetReminderHoursBeforeAsync(UserId)).ReturnsAsync(hours);

            var dueDate = DateTime.UtcNow.AddDays(30);

            await _sut.ScheduleTaskDueSoonNotification(TaskId, UserId, dueDate);

            var (_, state) = SingleCreatedJob();
            var scheduled = Assert.IsType<ScheduledState>(state);

            Assert.Equal(dueDate.AddHours(-hours), scheduled.EnqueueAt, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task ScheduleTaskDueSoonNotification_SchedulesTheDueSoonMethodForThatTask()
        {
            await _sut.ScheduleTaskDueSoonNotification(TaskId, UserId, DateTime.UtcNow.AddDays(5));

            var (job, _) = SingleCreatedJob();

            Assert.Equal(typeof(NotificationBackgroundJob), job.Type);
            Assert.Equal(nameof(NotificationBackgroundJob.SendTaskDueSoonNotification), job.Method.Name);
            Assert.Equal(TaskId, Assert.Single(job.Args));
        }

        /// <summary>
        /// A due date arriving with an unspecified kind is treated as UTC rather than as local
        /// time. Getting that wrong shifts every reminder by the server's offset, which is
        /// invisible on a machine set to UTC and wrong everywhere else.
        /// </summary>
        [Fact]
        public async Task ScheduleTaskDueSoonNotification_TreatsAnUnspecifiedDueDateAsUtc()
        {
            var unspecified = DateTime.SpecifyKind(
                DateTime.UtcNow.AddDays(10), DateTimeKind.Unspecified);

            await _sut.ScheduleTaskDueSoonNotification(TaskId, UserId, unspecified);

            var (_, state) = SingleCreatedJob();
            var scheduled = Assert.IsType<ScheduledState>(state);

            var expected = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc).AddHours(-24);
            Assert.Equal(expected, scheduled.EnqueueAt, TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// A reminder whose moment has already passed is not scheduled at all. Hangfire would
        /// otherwise fire it immediately, so a task created with a due date inside the reminder
        /// window would email the assignee the instant it was assigned.
        /// </summary>
        [Fact]
        public async Task ScheduleTaskDueSoonNotification_SchedulesNothingWhenTheReminderTimeHasPassed()
        {
            var dueDateInsideTheWindow = DateTime.UtcNow.AddHours(2);

            var jobId = await _sut.ScheduleTaskDueSoonNotification(TaskId, UserId, dueDateInsideTheWindow);

            Assert.Null(jobId);
            _jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
        }

        [Fact]
        public async Task ScheduleTaskDueSoonNotification_SchedulesNothingForADueDateAlreadyInThePast()
        {
            var jobId = await _sut.ScheduleTaskDueSoonNotification(
                TaskId, UserId, DateTime.UtcNow.AddDays(-1));

            Assert.Null(jobId);
            _jobs.Verify(j => j.Create(It.IsAny<Job>(), It.IsAny<IState>()), Times.Never);
        }

        /// <summary>
        /// Callers pass whatever DueSoonJobId happens to hold, which is null for any task that
        /// never had a reminder. Guarding here keeps that check out of every call site.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void CancelScheduledJob_IgnoresAnAbsentJobId(string? jobId)
        {
            _sut.CancelScheduledJob(jobId!);

            _jobs.Verify(
                j => j.ChangeState(It.IsAny<string>(), It.IsAny<IState>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public void CancelScheduledJob_DeletesTheJobItWasGiven()
        {
            _sut.CancelScheduledJob("job-to-cancel");

            _jobs.Verify(
                j => j.ChangeState("job-to-cancel", It.IsAny<DeletedState>(), It.IsAny<string>()),
                Times.Once);
        }

        [Fact]
        public void TriggerOverdueCheck_EnqueuesTheOverdueSweepImmediately()
        {
            _sut.TriggerOverdueCheck();

            var (job, state) = SingleCreatedJob();

            Assert.IsType<EnqueuedState>(state);
            Assert.Equal(typeof(NotificationBackgroundJob), job.Type);
            Assert.Equal(nameof(NotificationBackgroundJob.CheckOverdueTasksAndNotify), job.Method.Name);
        }

        /// <summary>
        /// AddOrUpdate is keyed by the recurring job id, so a typo does not update the existing
        /// registration, it silently creates a second one and the work runs twice.
        /// </summary>
        [Fact]
        public void SetupRecurringJobs_RegistersTheThreeJobsUnderTheirKnownIds()
        {
            _sut.SetupRecurringJobs();

            var registered = _recurringJobs.Invocations
                .Where(i => i.Method.Name == nameof(IRecurringJobManager.AddOrUpdate))
                .Select(i => (string)i.Arguments[0])
                .ToList();

            Assert.Equal(3, registered.Count);
            Assert.Equal(registered.Count, registered.Distinct().Count());
            Assert.Contains("check-overdue-tasks", registered);
            Assert.Contains("cleanup-old-notifications", registered);
            Assert.Contains("expire-onetime-premium", registered);
        }

        [Theory]
        [InlineData("check-overdue-tasks", "0 0 * * *")]
        [InlineData("expire-onetime-premium", "0 1 * * *")]
        [InlineData("cleanup-old-notifications", "0 2 * * 0")]
        public void SetupRecurringJobs_UsesTheIntendedSchedule(string jobId, string expectedCron)
        {
            _sut.SetupRecurringJobs();

            var call = Assert.Single(_recurringJobs.Invocations
                .Where(i => i.Method.Name == nameof(IRecurringJobManager.AddOrUpdate)
                            && (string)i.Arguments[0] == jobId));

            Assert.Contains(expectedCron, call.Arguments.Select(a => a?.ToString()));
        }

        [Fact]
        public void SetupRecurringJobs_PointsEachIdAtTheRightMethod()
        {
            _sut.SetupRecurringJobs();

            var byId = _recurringJobs.Invocations
                .Where(i => i.Method.Name == nameof(IRecurringJobManager.AddOrUpdate))
                .ToDictionary(i => (string)i.Arguments[0], i => (Job)i.Arguments[1]);

            Assert.Equal(
                nameof(NotificationBackgroundJob.CheckOverdueTasksAndNotify),
                byId["check-overdue-tasks"].Method.Name);

            Assert.Equal(
                nameof(NotificationBackgroundJob.CleanupOldNotifications),
                byId["cleanup-old-notifications"].Method.Name);

            Assert.Equal(
                nameof(PremiumBackgroundJob.ExpireOneTimePremiumAsync),
                byId["expire-onetime-premium"].Method.Name);
        }
    }
}
