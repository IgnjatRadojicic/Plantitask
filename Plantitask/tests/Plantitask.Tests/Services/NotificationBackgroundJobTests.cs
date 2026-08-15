using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class NotificationBackgroundJobTests : DbTestBase
    {
        private readonly Mock<IEmailService> _email = new();
        private readonly Mock<INotificationService> _notifications = new();

        public NotificationBackgroundJobTests(PostgresFixture fixture) : base(fixture)
        {
            _notifications
                .Setup(n => n.ShouldNotifyAsync(It.IsAny<Guid>(), It.IsAny<NotificationType>()))
                .ReturnsAsync(true);

            _notifications
                .Setup(n => n.ShouldEmailAsync(It.IsAny<Guid>(), It.IsAny<NotificationType>()))
                .ReturnsAsync(true);
        }

        private NotificationBackgroundJob NewSut(IApplicationDbContext context) => new(
            context,
            NullLogger<NotificationBackgroundJob>.Instance,
            _email.Object,
            _notifications.Object);

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        private async Task SeedTaskAsync(
            Guid id,
            Guid? assignedTo,
            DateTime? dueDate,
            TaskStatusItem status = TaskStatusItem.InProgress,
            string title = "Ship the release")
        {
            await using var db = NewContext();
            db.Tasks.Add(TestData.Task(
                GroupId, LeadId, title: title, status: status,
                assignedTo: assignedTo, dueDate: dueDate, id: id));
            await db.SaveChangesAsync();
        }

        private async Task<List<Notification>> ReadNotificationsAsync()
        {
            await using var db = NewContext();
            return await db.Notifications.ToListAsync();
        }

        [Fact]
        public async Task SendTaskDueSoonNotification_CreatesTheNotificationAndSendsTheEmail()
        {
            await SeedAsync();
            var dueDate = DateTime.UtcNow.AddHours(6);
            await SeedTaskAsync(TaskId, MemberId, dueDate);

            await using var act = NewContext();
            await NewSut(act).SendTaskDueSoonNotification(TaskId);

            var notification = Assert.Single(await ReadNotificationsAsync());
            Assert.Equal(MemberId, notification.UserId);
            Assert.Equal(NotificationType.TaskDueSoon, notification.Type);
            Assert.Equal(TaskId, notification.RelatedEntityId);
            Assert.Contains("Ship the release", notification.Message);

            _email.Verify(e => e.SendTaskDueSoonEmailAsync(
                "member@example.com", "member", "Ship the release", It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task SendTaskDueSoonNotification_DoesNothingWhenTheTaskIsGone()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).SendTaskDueSoonNotification(Guid.NewGuid());

            Assert.Empty(await ReadNotificationsAsync());
            _email.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The reminder was scheduled long before it fires, so the world has to be re-checked at
        /// send time. A task finished in the meantime must not produce a reminder.
        /// </summary>
        [Fact]
        public async Task SendTaskDueSoonNotification_SkipsATaskThatWasCompletedSinceScheduling()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, MemberId, DateTime.UtcNow.AddHours(6), TaskStatusItem.Completed);

            await using var act = NewContext();
            await NewSut(act).SendTaskDueSoonNotification(TaskId);

            Assert.Empty(await ReadNotificationsAsync());
            _email.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendTaskDueSoonNotification_SkipsATaskThatWasUnassignedSinceScheduling()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, assignedTo: null, dueDate: DateTime.UtcNow.AddHours(6));

            await using var act = NewContext();
            await NewSut(act).SendTaskDueSoonNotification(TaskId);

            Assert.Empty(await ReadNotificationsAsync());
            _email.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendTaskDueSoonNotification_HonoursTheInAppPreferenceSeparatelyFromEmail()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, MemberId, DateTime.UtcNow.AddHours(6));

            _notifications
                .Setup(n => n.ShouldNotifyAsync(MemberId, NotificationType.TaskDueSoon))
                .ReturnsAsync(false);

            await using var act = NewContext();
            await NewSut(act).SendTaskDueSoonNotification(TaskId);

            Assert.Empty(await ReadNotificationsAsync());

            _email.Verify(e => e.SendTaskDueSoonEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public async Task SendTaskDueSoonNotification_HonoursTheEmailPreferenceSeparatelyFromInApp()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, MemberId, DateTime.UtcNow.AddHours(6));

            _notifications
                .Setup(n => n.ShouldEmailAsync(MemberId, NotificationType.TaskDueSoon))
                .ReturnsAsync(false);

            await using var act = NewContext();
            await NewSut(act).SendTaskDueSoonNotification(TaskId);

            Assert.Single(await ReadNotificationsAsync());
            _email.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The notification commits before the email is attempted, so a mail provider outage
        /// must not undo the in app record or fail the job into a Hangfire retry.
        /// </summary>
        [Fact]
        public async Task SendTaskDueSoonNotification_KeepsTheNotificationWhenTheEmailFails()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, MemberId, DateTime.UtcNow.AddHours(6));

            _email
                .Setup(e => e.SendTaskDueSoonEmailAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
                .ThrowsAsync(new Plantitask.Core.Common.EmailSendException("provider down"));

            await using var act = NewContext();
            var thrown = await Record.ExceptionAsync(() => NewSut(act).SendTaskDueSoonNotification(TaskId));

            Assert.Null(thrown);
            Assert.Single(await ReadNotificationsAsync());
        }

        [Fact]
        public async Task CheckOverdueTasksAndNotify_SendsOneDigestPerUserNoMatterHowManyTasks()
        {
            await SeedAsync();
            var past = DateTime.UtcNow.AddDays(-3);
            await SeedTaskAsync(Guid.NewGuid(), MemberId, past, title: "First");
            await SeedTaskAsync(Guid.NewGuid(), MemberId, past.AddDays(-2), title: "Second");
            await SeedTaskAsync(Guid.NewGuid(), MemberId, past.AddDays(-4), title: "Third");

            await using var act = NewContext();
            await NewSut(act).CheckOverdueTasksAndNotify();

            var notification = Assert.Single(await ReadNotificationsAsync());
            Assert.Equal(NotificationType.TaskOverdue, notification.Type);
            Assert.Equal("You have 3 overdue tasks", notification.Message);

            _email.Verify(e => e.SendTaskOverdueDigestEmailAsync(
                "member@example.com", "member", 3, It.IsAny<IReadOnlyList<OverdueTaskLine>>()), Times.Once);
        }

        /// <summary>
        /// The email lists only the worst offenders while the count covers everything, so the
        /// message stays short no matter how bad the backlog is.
        /// </summary>
        [Fact]
        public async Task CheckOverdueTasksAndNotify_CapsTheListedTasksButNotTheCount()
        {
            await SeedAsync();
            var past = DateTime.UtcNow.AddDays(-1);
            await SeedTaskAsync(Guid.NewGuid(), MemberId, past.AddDays(-5), title: "Worst");
            await SeedTaskAsync(Guid.NewGuid(), MemberId, past.AddDays(-3), title: "Middle");
            await SeedTaskAsync(Guid.NewGuid(), MemberId, past, title: "Least bad");

            IReadOnlyList<OverdueTaskLine>? listed = null;
            _email
                .Setup(e => e.SendTaskOverdueDigestEmailAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<OverdueTaskLine>>()))
                .Callback<string, string, int, IReadOnlyList<OverdueTaskLine>>((_, _, _, lines) => listed = lines)
                .Returns(Task.CompletedTask);

            await using var act = NewContext();
            await NewSut(act).CheckOverdueTasksAndNotify();

            Assert.NotNull(listed);
            Assert.Equal(2, listed.Count);
            Assert.Equal(new[] { "Worst", "Middle" }, listed.Select(l => l.Title));
        }

        [Fact]
        public async Task CheckOverdueTasksAndNotify_GivesEachUserTheirOwnDigest()
        {
            await SeedAsync();
            var past = DateTime.UtcNow.AddDays(-2);
            await SeedTaskAsync(Guid.NewGuid(), MemberId, past, title: "Theirs");
            await SeedTaskAsync(Guid.NewGuid(), LeadId, past, title: "Mine");

            await using var act = NewContext();
            await NewSut(act).CheckOverdueTasksAndNotify();

            var notifications = await ReadNotificationsAsync();

            Assert.Equal(2, notifications.Count);
            Assert.Contains(notifications, n => n.UserId == MemberId);
            Assert.Contains(notifications, n => n.UserId == LeadId);
        }

        /// <summary>
        /// The digest log is what makes a Hangfire retry safe. Without it a re run on the same
        /// day would notify and email everybody a second time.
        /// </summary>
        [Fact]
        public async Task CheckOverdueTasksAndNotify_DoesNotDigestTheSameUserTwiceInADay()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, MemberId, DateTime.UtcNow.AddDays(-2));

            await using (var first = NewContext())
                await NewSut(first).CheckOverdueTasksAndNotify();

            await using (var second = NewContext())
                await NewSut(second).CheckOverdueTasksAndNotify();

            Assert.Single(await ReadNotificationsAsync());

            _email.Verify(e => e.SendTaskOverdueDigestEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<OverdueTaskLine>>()), Times.Once);
        }

        [Fact]
        public async Task CheckOverdueTasksAndNotify_RecordsThatTheUserWasDigestedToday()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, MemberId, DateTime.UtcNow.AddDays(-2));

            await using var act = NewContext();
            await NewSut(act).CheckOverdueTasksAndNotify();

            await using var assert = NewContext();
            var log = Assert.Single(await assert.NotificationDigestLogs.ToListAsync());

            Assert.Equal(MemberId, log.UserId);
            Assert.Equal(NotificationType.TaskOverdue, log.Type);
            Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), log.SentOn);
        }

        [Theory]
        [InlineData(TaskStatusItem.Completed)]
        public async Task CheckOverdueTasksAndNotify_IgnoresCompletedTasks(TaskStatusItem status)
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, MemberId, DateTime.UtcNow.AddDays(-2), status);

            await using var act = NewContext();
            await NewSut(act).CheckOverdueTasksAndNotify();

            Assert.Empty(await ReadNotificationsAsync());
        }

        [Fact]
        public async Task CheckOverdueTasksAndNotify_IgnoresTasksThatAreNotDueYetOrHaveNoDueDate()
        {
            await SeedAsync();
            await SeedTaskAsync(Guid.NewGuid(), MemberId, DateTime.UtcNow.AddDays(5), title: "Future");
            await SeedTaskAsync(Guid.NewGuid(), MemberId, dueDate: null, title: "No due date");

            await using var act = NewContext();
            await NewSut(act).CheckOverdueTasksAndNotify();

            Assert.Empty(await ReadNotificationsAsync());
        }

        [Fact]
        public async Task CheckOverdueTasksAndNotify_IgnoresOverdueTasksWithNobodyAssigned()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, assignedTo: null, dueDate: DateTime.UtcNow.AddDays(-2));

            await using var act = NewContext();
            await NewSut(act).CheckOverdueTasksAndNotify();

            Assert.Empty(await ReadNotificationsAsync());
            _email.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The two preferences are read from the database here rather than through the
        /// notification service, so each has to be honoured independently.
        /// </summary>
        [Fact]
        public async Task CheckOverdueTasksAndNotify_SkipsTheInAppRowWhenThatPreferenceIsOff()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, MemberId, DateTime.UtcNow.AddDays(-2));

            await using (var db = NewContext())
            {
                db.NotificationPreferences.Add(new NotificationPreference
                {
                    UserId = MemberId,
                    Type = NotificationType.TaskOverdue,
                    IsEnabled = false,
                    IsEmailEnabled = true
                });
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            await NewSut(act).CheckOverdueTasksAndNotify();

            Assert.Empty(await ReadNotificationsAsync());

            _email.Verify(e => e.SendTaskOverdueDigestEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<IReadOnlyList<OverdueTaskLine>>()), Times.Once);
        }

        [Fact]
        public async Task CheckOverdueTasksAndNotify_SkipsTheEmailWhenThatPreferenceIsOff()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, MemberId, DateTime.UtcNow.AddDays(-2));

            await using (var db = NewContext())
            {
                db.NotificationPreferences.Add(new NotificationPreference
                {
                    UserId = MemberId,
                    Type = NotificationType.TaskOverdue,
                    IsEnabled = true,
                    IsEmailEnabled = false
                });
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            await NewSut(act).CheckOverdueTasksAndNotify();

            Assert.Single(await ReadNotificationsAsync());
            _email.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CheckOverdueTasksAndNotify_PhrasesASingleOverdueTaskSpecifically()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, MemberId, DateTime.UtcNow.AddDays(-2), title: "Ship the release");

            await using var act = NewContext();
            await NewSut(act).CheckOverdueTasksAndNotify();

            var notification = Assert.Single(await ReadNotificationsAsync());
            Assert.Equal("'Ship the release' is overdue by 2 days", notification.Message);
        }

        [Fact]
        public async Task CheckOverdueTasksAndNotify_StillMarksTheDigestWhenTheEmailFails()
        {
            await SeedAsync();
            await SeedTaskAsync(TaskId, MemberId, DateTime.UtcNow.AddDays(-2));

            _email
                .Setup(e => e.SendTaskOverdueDigestEmailAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(),
                    It.IsAny<IReadOnlyList<OverdueTaskLine>>()))
                .ThrowsAsync(new Plantitask.Core.Common.EmailSendException("provider down"));

            await using var act = NewContext();
            var thrown = await Record.ExceptionAsync(() => NewSut(act).CheckOverdueTasksAndNotify());

            Assert.Null(thrown);

            await using var assert = NewContext();
            Assert.Single(await assert.NotificationDigestLogs.ToListAsync());
        }

        [Fact]
        public async Task CheckOverdueTasksAndNotify_DoesNothingWhenNothingIsOverdue()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).CheckOverdueTasksAndNotify();

            Assert.Empty(await ReadNotificationsAsync());

            await using var assert = NewContext();
            Assert.Empty(await assert.NotificationDigestLogs.ToListAsync());
        }

        [Fact]
        public async Task CleanupOldNotifications_SoftDeletesNotificationsReadLongerAgoThanTheWindow()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                db.Notifications.AddRange(
                    NotificationRead(MemberId, "old", DateTime.UtcNow.AddDays(-45)),
                    NotificationRead(MemberId, "recent", DateTime.UtcNow.AddDays(-5)));
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            await NewSut(act).CleanupOldNotifications();

            await using var assert = NewContext();
            var alive = Assert.Single(await assert.Notifications.ToListAsync());
            Assert.Equal("recent", alive.Title);

            var removed = await assert.Notifications
                .IgnoreQueryFilters()
                .SingleAsync(n => n.Title == "old");

            Assert.True(removed.IsDeleted);
            Assert.NotNull(removed.DeletedAt);
            Assert.NotNull(removed.UpdatedAt);
        }

        [Fact]
        public async Task CleanupOldNotifications_LeavesUnreadNotificationsAloneHoweverOldTheyAre()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                var unread = NotificationRead(MemberId, "never read", DateTime.UtcNow.AddDays(-400));
                unread.IsRead = false;
                unread.ReadAt = null;
                db.Notifications.Add(unread);
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            await NewSut(act).CleanupOldNotifications();

            Assert.Single(await ReadNotificationsAsync());
        }

        [Fact]
        public async Task CleanupOldNotifications_HardDeletesDigestLogRowsPastTheirWindow()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                db.NotificationDigestLogs.AddRange(
                    new NotificationDigestLog
                    {
                        UserId = MemberId,
                        Type = NotificationType.TaskOverdue,
                        SentOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60))
                    },
                    new NotificationDigestLog
                    {
                        UserId = MemberId,
                        Type = NotificationType.TaskOverdue,
                        SentOn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2))
                    });
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            await NewSut(act).CleanupOldNotifications();

            await using var assert = NewContext();
            var remaining = Assert.Single(await assert.NotificationDigestLogs.ToListAsync());

            Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), remaining.SentOn);
        }

        private static Notification NotificationRead(Guid userId, string title, DateTime readAt) => new()
        {
            UserId = userId,
            Type = NotificationType.TaskAssigned,
            Title = title,
            Message = "message",
            RelatedEntityType = "Task",
            IsRead = true,
            ReadAt = readAt
        };
    }
}
