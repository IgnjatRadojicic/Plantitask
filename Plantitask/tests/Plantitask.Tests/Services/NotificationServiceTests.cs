using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Plantitask.Core.DTO.Comments;
using Plantitask.Core.DTO.Notifications;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class NotificationServiceTests : DbTestBase
    {
        private readonly Mock<IEmailService> _email = new();

        public NotificationServiceTests(PostgresFixture fixture) : base(fixture) { }

        private NotificationService NewSut(IApplicationDbContext context) => new(
            context, NullLogger<NotificationService>.Instance, _email.Object);

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        private static TaskDto TaskDto(
            Guid? assignedTo = null,
            Guid? createdBy = null,
            string title = "Ship the release") => new()
            {
                Id = TaskId,
                Title = title,
                GroupId = GroupId,
                AssignedToId = assignedTo,
                CreatedBy = createdBy ?? LeadId
            };

        private async Task SetPreferenceAsync(
            Guid userId, NotificationType type, bool inApp = true, bool byEmail = true, int? reminderHours = null)
        {
            await using var db = NewContext();
            db.NotificationPreferences.Add(new NotificationPreference
            {
                UserId = userId,
                Type = type,
                IsEnabled = inApp,
                IsEmailEnabled = byEmail,
                ReminderHoursBefore = reminderHours
            });
            await db.SaveChangesAsync();
        }

        private async Task<Guid> SeedNotificationAsync(
            Guid userId,
            bool isRead = false,
            string title = "Seeded",
            NotificationType type = NotificationType.TaskAssigned,
            DateTime? createdAt = null)
        {
            await using var db = NewContext();

            var notification = new Notification
            {
                UserId = userId,
                Type = type,
                Title = title,
                Message = "message",
                RelatedEntityType = "Task",
                IsRead = isRead,
                ReadAt = isRead ? DateTime.UtcNow : null
            };

            db.Notifications.Add(notification);
            await db.SaveChangesAsync();

            if (createdAt.HasValue)
                await db.BackdateAsync<Notification>(notification.Id, createdAt.Value);

            return notification.Id;
        }

        private async Task<List<Notification>> ReadAllAsync()
        {
            await using var db = NewContext();
            return await db.Notifications.ToListAsync();
        }

        [Fact]
        public async Task NotifyAssignmentAsync_NotifiesTheAssigneeAndNamesTheActor()
        {
            await SeedAsync();

            await using var act = NewContext();
            var dto = await NewSut(act).NotifyAssignmentAsync(LeadId, TaskDto(assignedTo: MemberId));

            Assert.NotNull(dto);
            Assert.Equal(MemberId, dto.UserId);
            Assert.Equal(LeadId, dto.ActorId);
            Assert.Equal("lead", dto.ActorName);
            Assert.Equal(NotificationType.TaskAssigned, dto.Type);
            Assert.Equal(TaskId, dto.RelatedEntityId);
            Assert.Contains("Ship the release", dto.Message);

            Assert.Single(await ReadAllAsync());
        }

        /// <summary>
        /// Assigning something to yourself should not notify you about it. You already know.
        /// </summary>
        [Fact]
        public async Task NotifyAssignmentAsync_SaysNothingWhenTheActorAssignedThemselves()
        {
            await SeedAsync();

            await using var act = NewContext();
            var dto = await NewSut(act).NotifyAssignmentAsync(MemberId, TaskDto(assignedTo: MemberId));

            Assert.Null(dto);
            Assert.Empty(await ReadAllAsync());
        }

        [Fact]
        public async Task NotifyAssignmentAsync_SaysNothingWhenThereIsNoAssignee()
        {
            await SeedAsync();

            await using var act = NewContext();
            var dto = await NewSut(act).NotifyAssignmentAsync(LeadId, TaskDto(assignedTo: null));

            Assert.Null(dto);
            Assert.Empty(await ReadAllAsync());
        }

        [Fact]
        public async Task NotifyAssignmentAsync_SaysNothingWhenTheRecipientTurnedTheTypeOff()
        {
            await SeedAsync();
            await SetPreferenceAsync(MemberId, NotificationType.TaskAssigned, inApp: false);

            await using var act = NewContext();
            var dto = await NewSut(act).NotifyAssignmentAsync(LeadId, TaskDto(assignedTo: MemberId));

            Assert.Null(dto);
            Assert.Empty(await ReadAllAsync());
        }

        /// <summary>
        /// The assignee and the creator both hear about a status change, and the person who made
        /// it does not. One save covers the whole fan out, which is the shape that replaced the
        /// old save per recipient.
        /// </summary>
        [Fact]
        public async Task NotifyTaskStatusChangedAsync_ReachesTheAssigneeAndTheCreatorButNotTheActor()
        {
            await SeedAsync();

            await using var act = NewContext();
            var dtos = await NewSut(act).NotifyTaskStatusChangedAsync(
                OtherLeadId, TaskDto(assignedTo: MemberId, createdBy: LeadId), "Not Started", "In Progress");

            Assert.Equal(2, dtos.Count);
            Assert.Contains(dtos, d => d.UserId == MemberId);
            Assert.Contains(dtos, d => d.UserId == LeadId);
            Assert.DoesNotContain(dtos, d => d.UserId == OtherLeadId);

            Assert.Contains("from Not Started to In Progress", dtos[0].Message);
            Assert.Equal(2, (await ReadAllAsync()).Count);
        }

        [Fact]
        public async Task NotifyTaskStatusChangedAsync_SendsOnlyOneWhenTheCreatorIsAlsoTheAssignee()
        {
            await SeedAsync();

            await using var act = NewContext();
            var dtos = await NewSut(act).NotifyTaskStatusChangedAsync(
                OtherLeadId, TaskDto(assignedTo: LeadId, createdBy: LeadId), "Not Started", "In Progress");

            Assert.Equal(LeadId, Assert.Single(dtos).UserId);
        }

        [Fact]
        public async Task NotifyTaskStatusChangedAsync_SaysNothingWhenTheActorIsTheOnlyPersonInvolved()
        {
            await SeedAsync();

            await using var act = NewContext();
            var dtos = await NewSut(act).NotifyTaskStatusChangedAsync(
                LeadId, TaskDto(assignedTo: LeadId, createdBy: LeadId), "Not Started", "In Progress");

            Assert.Empty(dtos);
            Assert.Empty(await ReadAllAsync());
        }

        [Fact]
        public async Task NotifyTaskStatusChangedAsync_DropsOnlyTheRecipientWhoTurnedTheTypeOff()
        {
            await SeedAsync();
            await SetPreferenceAsync(MemberId, NotificationType.TaskStatusChanged, inApp: false);

            await using var act = NewContext();
            var dtos = await NewSut(act).NotifyTaskStatusChangedAsync(
                OtherLeadId, TaskDto(assignedTo: MemberId, createdBy: LeadId), "Not Started", "In Progress");

            Assert.Equal(LeadId, Assert.Single(dtos).UserId);
        }

        [Fact]
        public async Task NotifyTaskCommentAddedAsync_ReachesTheCreatorAndAssigneeButNotTheCommenter()
        {
            await SeedAsync();

            var comment = new CommentDto { UserId = MemberId, UserName = "member", Content = "looks good" };

            await using var act = NewContext();
            var dtos = await NewSut(act).NotifyTaskCommentAddedAsync(
                GroupId, TaskDto(assignedTo: MemberId, createdBy: LeadId), comment);

            var dto = Assert.Single(dtos);
            Assert.Equal(LeadId, dto.UserId);
            Assert.Equal(MemberId, dto.ActorId);
            Assert.Equal("member", dto.ActorName);
        }

        [Fact]
        public async Task NotifyTaskCommentAddedAsync_SaysNothingWhenTheCommenterIsTheOnlyPersonInvolved()
        {
            await SeedAsync();

            var comment = new CommentDto { UserId = LeadId, UserName = "lead", Content = "note to self" };

            await using var act = NewContext();
            var dtos = await NewSut(act).NotifyTaskCommentAddedAsync(
                GroupId, TaskDto(assignedTo: LeadId, createdBy: LeadId), comment);

            Assert.Empty(dtos);
        }

        [Fact]
        public async Task NotifyTaskPriorityChangedAsync_TellsTheAssigneeBothNames()
        {
            await SeedAsync();

            await using var act = NewContext();
            var dto = await NewSut(act).NotifyTaskPriorityChangedAsync(
                LeadId, TaskDto(assignedTo: MemberId), "Low", "Urgent");

            Assert.NotNull(dto);
            Assert.Contains("from Low to Urgent", dto.Message);
        }

        [Fact]
        public async Task NotifyTaskUpdatedAsync_TellsTheAssignee()
        {
            await SeedAsync();

            await using var act = NewContext();
            var dto = await NewSut(act).NotifyTaskUpdatedAsync(LeadId, TaskDto(assignedTo: MemberId));

            Assert.NotNull(dto);
            Assert.Equal(MemberId, dto.UserId);
            Assert.Equal(NotificationType.TaskUpdated, dto.Type);
        }

        /// <summary>
        /// Joining a group is something you did yourself, so the notification carries no actor.
        /// </summary>
        [Fact]
        public async Task NotifyGroupInvitationAsync_HasNoActor()
        {
            await SeedAsync();

            await using var act = NewContext();
            var dto = await NewSut(act).NotifyGroupInvitationAsync(MemberId, "Dev Team");

            Assert.NotNull(dto);
            Assert.Null(dto.ActorId);
            Assert.Null(dto.ActorName);
            Assert.Contains("Dev Team", dto.Message);
        }

        [Fact]
        public async Task GetUserNotificationsAsync_ReturnsOnlyTheCallersOwnNewestFirst()
        {
            await SeedAsync();
            var now = DateTime.UtcNow;
            await SeedNotificationAsync(MemberId, title: "oldest", createdAt: now.AddHours(-3));
            await SeedNotificationAsync(MemberId, title: "newest", createdAt: now);
            await SeedNotificationAsync(LeadId, title: "someone elses", createdAt: now.AddHours(-1));

            await using var act = NewContext();
            var result = await NewSut(act).GetUserNotificationsAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(new[] { "newest", "oldest" }, result.Value!.Items.Select(n => n.Title));
        }

        [Fact]
        public async Task GetUserNotificationsAsync_CanReturnUnreadOnly()
        {
            await SeedAsync();
            await SeedNotificationAsync(MemberId, isRead: false, title: "unread");
            await SeedNotificationAsync(MemberId, isRead: true, title: "read");

            await using var act = NewContext();
            var result = await NewSut(act).GetUserNotificationsAsync(MemberId, unreadOnly: true);

            Assert.Equal("unread", Assert.Single(result.Value!.Items).Title);
        }

        [Fact]
        public async Task GetUserNotificationsAsync_PagesAndCountsBeforeSkipTake()
        {
            await SeedAsync();
            var now = DateTime.UtcNow;
            for (var i = 0; i < 7; i++)
                await SeedNotificationAsync(MemberId, title: $"n{i}", createdAt: now.AddMinutes(-i));

            await using var act = NewContext();
            var result = await NewSut(act).GetUserNotificationsAsync(MemberId, pageNumber: 2, pageSize: 3);

            Assert.Equal(7, result.Value!.TotalCount);
            Assert.Equal(new[] { "n3", "n4", "n5" }, result.Value.Items.Select(n => n.Title));
        }

        [Fact]
        public async Task GetUnreadCountAsync_CountsOnlyTheCallersUnread()
        {
            await SeedAsync();
            await SeedNotificationAsync(MemberId, isRead: false);
            await SeedNotificationAsync(MemberId, isRead: false);
            await SeedNotificationAsync(MemberId, isRead: true);
            await SeedNotificationAsync(LeadId, isRead: false);

            await using var act = NewContext();
            var result = await NewSut(act).GetUnreadCountAsync(MemberId);

            Assert.Equal(2, result.Value!.Count);
        }

        [Fact]
        public async Task MarkAsReadAsync_MarksTheCallersOwnNotification()
        {
            await SeedAsync();
            var id = await SeedNotificationAsync(MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).MarkAsReadAsync(id, MemberId);

            Assert.True(result.IsSuccess);

            await using var assert = NewContext();
            var notification = await assert.Notifications.SingleAsync();
            Assert.True(notification.IsRead);
            Assert.NotNull(notification.ReadAt);
            Assert.NotNull(notification.UpdatedAt);
        }

        /// <summary>
        /// Somebody else's id is a silent no op rather than a NotFound. The owner is part of the
        /// update predicate, so the caller learns nothing about whether that id exists.
        /// </summary>
        [Fact]
        public async Task MarkAsReadAsync_LeavesSomebodyElsesNotificationAloneAndSaysNothingAboutIt()
        {
            await SeedAsync();
            var id = await SeedNotificationAsync(LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).MarkAsReadAsync(id, MemberId);

            Assert.True(result.IsSuccess);

            await using var assert = NewContext();
            Assert.False((await assert.Notifications.SingleAsync()).IsRead);
        }

        [Fact]
        public async Task MarkAllAsReadAsync_MarksEveryUnreadOfTheCallersAndNobodyElses()
        {
            await SeedAsync();
            await SeedNotificationAsync(MemberId, isRead: false);
            await SeedNotificationAsync(MemberId, isRead: false);
            var theirs = await SeedNotificationAsync(LeadId, isRead: false);

            await using var act = NewContext();
            await NewSut(act).MarkAllAsReadAsync(MemberId);

            await using var assert = NewContext();
            Assert.All(
                await assert.Notifications.Where(n => n.UserId == MemberId).ToListAsync(),
                n => Assert.True(n.IsRead));

            Assert.False((await assert.Notifications.SingleAsync(n => n.Id == theirs)).IsRead);
        }

        [Fact]
        public async Task DeleteNotificationAsync_SoftDeletesTheCallersOwn()
        {
            await SeedAsync();
            var id = await SeedNotificationAsync(MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteNotificationAsync(id, MemberId);

            Assert.True(result.IsSuccess);

            await using var assert = NewContext();
            Assert.Empty(await assert.Notifications.ToListAsync());

            var stored = await assert.Notifications.IgnoreQueryFilters().SingleAsync();
            Assert.True(stored.IsDeleted);
            Assert.Equal(MemberId, stored.DeletedBy);
        }

        /// <summary>
        /// Delete answers NotFound for somebody else's id because the owner filter is part of the
        /// lookup, so a caller cannot tell a foreign notification from one that does not exist.
        /// </summary>
        [Fact]
        public async Task DeleteNotificationAsync_ReturnsNotFoundForSomebodyElsesNotification()
        {
            await SeedAsync();
            var id = await SeedNotificationAsync(LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteNotificationAsync(id, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Single(await assert.Notifications.ToListAsync());
        }

        /// <summary>
        /// The sheet always lists every type. Anything the user has never touched shows the
        /// defaults rather than being missing from the screen.
        /// </summary>
        [Fact]
        public async Task GetUserPreferencesAsync_ListsEveryTypeWithDefaultsForTheUntouchedOnes()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetUserPreferencesAsync(MemberId);

            var preferences = result.Value!;

            Assert.Equal(Enum.GetValues<NotificationType>().Length, preferences.Count);
            Assert.All(preferences, p => Assert.True(p.IsEnabled));
            Assert.All(preferences, p => Assert.True(p.IsEmailEnabled));
            Assert.All(preferences, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
        }

        [Fact]
        public async Task GetUserPreferencesAsync_DefaultsTheDueSoonReminderToTwentyFourHoursAndTheRestToNothing()
        {
            await SeedAsync();

            await using var act = NewContext();
            var preferences = (await NewSut(act).GetUserPreferencesAsync(MemberId)).Value!;

            Assert.Equal(24, preferences.Single(p => p.Type == NotificationType.TaskDueSoon).ReminderHoursBefore);
            Assert.Null(preferences.Single(p => p.Type == NotificationType.TaskAssigned).ReminderHoursBefore);
        }

        [Fact]
        public async Task GetUserPreferencesAsync_ShowsTheStoredValueWhereThereIsOne()
        {
            await SeedAsync();
            await SetPreferenceAsync(MemberId, NotificationType.TaskDueSoon, inApp: false, byEmail: false, reminderHours: 48);

            await using var act = NewContext();
            var preferences = (await NewSut(act).GetUserPreferencesAsync(MemberId)).Value!;

            var dueSoon = preferences.Single(p => p.Type == NotificationType.TaskDueSoon);
            Assert.False(dueSoon.IsEnabled);
            Assert.False(dueSoon.IsEmailEnabled);
            Assert.Equal(48, dueSoon.ReminderHoursBefore);
        }

        [Fact]
        public async Task SaveUserPreferencesAsync_InsertsARowForATypeWithNoneYet()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).SaveUserPreferencesAsync(MemberId, new UpdateNotificationPreferencesDto
            {
                Preferences =
                [
                    new NotificationPreferenceUpdateItem
                    {
                        Type = NotificationType.TaskDueSoon,
                        IsEnabled = false,
                        IsEmailEnabled = false,
                        ReminderHoursBefore = 48
                    }
                ]
            });

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            var stored = await assert.NotificationPreferences.SingleAsync();
            Assert.Equal(MemberId, stored.UserId);
            Assert.False(stored.IsEnabled);
            Assert.Equal(48, stored.ReminderHoursBefore);
        }

        [Fact]
        public async Task SaveUserPreferencesAsync_UpdatesTheExistingRowRatherThanAddingASecond()
        {
            await SeedAsync();
            await SetPreferenceAsync(MemberId, NotificationType.TaskDueSoon, inApp: true, reminderHours: 24);

            await using var act = NewContext();
            await NewSut(act).SaveUserPreferencesAsync(MemberId, new UpdateNotificationPreferencesDto
            {
                Preferences =
                [
                    new NotificationPreferenceUpdateItem
                    {
                        Type = NotificationType.TaskDueSoon,
                        IsEnabled = false,
                        IsEmailEnabled = true,
                        ReminderHoursBefore = 72
                    }
                ]
            });

            await using var assert = NewContext();
            var stored = Assert.Single(await assert.NotificationPreferences.ToListAsync());
            Assert.False(stored.IsEnabled);
            Assert.Equal(72, stored.ReminderHoursBefore);
        }

        /// <summary>
        /// The whole batch is validated before anything is written, so one bad item cannot leave
        /// the earlier ones half applied.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(169)]
        [InlineData(-5)]
        public async Task SaveUserPreferencesAsync_RejectsReminderHoursOutsideTheAllowedRange(int hours)
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).SaveUserPreferencesAsync(MemberId, new UpdateNotificationPreferencesDto
            {
                Preferences =
                [
                    new NotificationPreferenceUpdateItem
                    {
                        Type = NotificationType.TaskAssigned, IsEnabled = false, IsEmailEnabled = false
                    },
                    new NotificationPreferenceUpdateItem
                    {
                        Type = NotificationType.TaskDueSoon,
                        IsEnabled = true,
                        IsEmailEnabled = true,
                        ReminderHoursBefore = hours
                    }
                ]
            });

            Assert.True(result.IsFailure);

            await using var assert = NewContext();
            Assert.Empty(await assert.NotificationPreferences.ToListAsync());
        }

        [Fact]
        public async Task SaveUserPreferencesAsync_AllowsANullReminderMeaningNoOverride()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).SaveUserPreferencesAsync(MemberId, new UpdateNotificationPreferencesDto
            {
                Preferences =
                [
                    new NotificationPreferenceUpdateItem
                    {
                        Type = NotificationType.TaskDueSoon,
                        IsEnabled = true,
                        IsEmailEnabled = true,
                        ReminderHoursBefore = null
                    }
                ]
            });

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            Assert.Null((await assert.NotificationPreferences.SingleAsync()).ReminderHoursBefore);
        }

        /// <summary>
        /// The type arrives as a client supplied enum, and an int outside the defined values
        /// casts happily. Enum.IsDefined is what stops a preference row for a type that does not
        /// exist.
        /// </summary>
        [Fact]
        public async Task SaveUserPreferencesAsync_RejectsATypeOutsideTheEnum()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).SaveUserPreferencesAsync(MemberId, new UpdateNotificationPreferencesDto
            {
                Preferences =
                [
                    new NotificationPreferenceUpdateItem
                    {
                        Type = (NotificationType)999, IsEnabled = true, IsEmailEnabled = true
                    }
                ]
            });

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Empty(await assert.NotificationPreferences.ToListAsync());
        }

        /// <summary>
        /// Notifications are opt out, so a user who has never opened the preferences screen gets
        /// everything. A missing row has to read as enabled rather than as disabled.
        /// </summary>
        [Fact]
        public async Task ShouldNotifyAndShouldEmail_DefaultToYesWithNoStoredRow()
        {
            await SeedAsync();

            await using var act = NewContext();
            var sut = NewSut(act);

            Assert.True(await sut.ShouldNotifyAsync(MemberId, NotificationType.TaskAssigned));
            Assert.True(await sut.ShouldEmailAsync(MemberId, NotificationType.TaskAssigned));
        }

        [Fact]
        public async Task ShouldNotifyAndShouldEmail_AreIndependentOfEachOther()
        {
            await SeedAsync();
            await SetPreferenceAsync(MemberId, NotificationType.TaskAssigned, inApp: true, byEmail: false);

            await using var act = NewContext();
            var sut = NewSut(act);

            Assert.True(await sut.ShouldNotifyAsync(MemberId, NotificationType.TaskAssigned));
            Assert.False(await sut.ShouldEmailAsync(MemberId, NotificationType.TaskAssigned));
        }

        [Fact]
        public async Task ShouldNotifyAsync_OnlyAnswersForTheTypeItWasAskedAbout()
        {
            await SeedAsync();
            await SetPreferenceAsync(MemberId, NotificationType.TaskAssigned, inApp: false);

            await using var act = NewContext();
            var sut = NewSut(act);

            Assert.False(await sut.ShouldNotifyAsync(MemberId, NotificationType.TaskAssigned));
            Assert.True(await sut.ShouldNotifyAsync(MemberId, NotificationType.TaskUpdated));
        }

        [Fact]
        public async Task GetReminderHoursBeforeAsync_DefaultsToTwentyFour()
        {
            await SeedAsync();

            await using var act = NewContext();
            Assert.Equal(24, await NewSut(act).GetReminderHoursBeforeAsync(MemberId));
        }

        [Fact]
        public async Task GetReminderHoursBeforeAsync_UsesTheStoredOverride()
        {
            await SeedAsync();
            await SetPreferenceAsync(MemberId, NotificationType.TaskDueSoon, reminderHours: 72);

            await using var act = NewContext();
            Assert.Equal(72, await NewSut(act).GetReminderHoursBeforeAsync(MemberId));
        }

        /// <summary>
        /// A stored row whose reminder column is null still means the default. The nullable cast
        /// is what keeps that distinguishable from a missing row rather than collapsing to zero.
        /// </summary>
        [Fact]
        public async Task GetReminderHoursBeforeAsync_FallsBackToTwentyFourWhenTheStoredRowHasNoOverride()
        {
            await SeedAsync();
            await SetPreferenceAsync(MemberId, NotificationType.TaskDueSoon, reminderHours: null);

            await using var act = NewContext();
            Assert.Equal(24, await NewSut(act).GetReminderHoursBeforeAsync(MemberId));
        }

        [Fact]
        public async Task GetUserContactAsync_ReturnsTheEmailAndUserName()
        {
            await SeedAsync();

            await using var act = NewContext();
            var contact = await NewSut(act).GetUserContactAsync(MemberId);

            Assert.NotNull(contact);
            Assert.Equal("member@example.com", contact.Value.Email);
            Assert.Equal("member", contact.Value.UserName);
        }

        [Fact]
        public async Task GetUserContactAsync_ReturnsNullForAUserWhoDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            Assert.Null(await NewSut(act).GetUserContactAsync(Guid.NewGuid()));
        }

        [Fact]
        public async Task TrySendTaskAssignmentEmailAsync_SendsToTheAssignee()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).TrySendTaskAssignmentEmailAsync(MemberId, "Ship it", "Dev Team", "lead");

            _email.Verify(e => e.SendTaskAssignmentEmailAsync(
                "member@example.com", "member", "Ship it", "Dev Team", "lead"), Times.Once);
        }

        [Fact]
        public async Task TrySendTaskAssignmentEmailAsync_RespectsTheEmailPreference()
        {
            await SeedAsync();
            await SetPreferenceAsync(MemberId, NotificationType.TaskAssigned, byEmail: false);

            await using var act = NewContext();
            await NewSut(act).TrySendTaskAssignmentEmailAsync(MemberId, "Ship it", "Dev Team", "lead");

            _email.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The Try prefix is the contract. These run after the mutation has committed, so a mail
        /// failure is logged and swallowed rather than surfacing to a caller who can no longer do
        /// anything about it.
        /// </summary>
        [Fact]
        public async Task TrySendTaskAssignmentEmailAsync_SwallowsAFailedSend()
        {
            await SeedAsync();

            _email
                .Setup(e => e.SendTaskAssignmentEmailAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Plantitask.Core.Common.EmailSendException("provider down"));

            await using var act = NewContext();
            var thrown = await Record.ExceptionAsync(
                () => NewSut(act).TrySendTaskAssignmentEmailAsync(MemberId, "Ship it", "Dev Team", "lead"));

            Assert.Null(thrown);
        }

        [Fact]
        public async Task TrySendCommentEmailAsync_SendsToTheAssigneeNamingTheCommenter()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).TrySendCommentEmailAsync(MemberId, LeadId, "Ship it", "looks good");

            _email.Verify(e => e.SendTaskCommentEmailAsync(
                "member@example.com", "member", "lead", "Ship it", "looks good"), Times.Once);
        }

        [Fact]
        public async Task TrySendCommentEmailAsync_SaysNothingWhenYouCommentOnYourOwnTask()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).TrySendCommentEmailAsync(MemberId, MemberId, "Ship it", "note to self");

            _email.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task TrySendCommentEmailAsync_SaysNothingWhenTheTaskHasNoAssignee()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).TrySendCommentEmailAsync(Guid.Empty, LeadId, "Ship it", "anyone there");

            _email.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task TrySendCommentEmailAsync_SwallowsAFailedSend()
        {
            await SeedAsync();

            _email
                .Setup(e => e.SendTaskCommentEmailAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Plantitask.Core.Common.EmailSendException("provider down"));

            await using var act = NewContext();
            var thrown = await Record.ExceptionAsync(
                () => NewSut(act).TrySendCommentEmailAsync(MemberId, LeadId, "Ship it", "looks good"));

            Assert.Null(thrown);
        }
    }
}
