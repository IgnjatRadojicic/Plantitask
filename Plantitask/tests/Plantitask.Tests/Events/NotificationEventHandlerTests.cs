using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Plantitask.Contracts.DTO.Notifications;
using Plantitask.Contracts.Enums;
using Plantitask.Core.Events;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Data;
using Plantitask.Infrastructure.Events.Handlers;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Events
{
    public class NotificationEventHandlerTests : DbTestBase
    {
        private readonly List<NotificationDto> _pushed = new();
        private readonly Mock<INotificationBroadcaster> _broadcaster = new();

        public NotificationEventHandlerTests(PostgresFixture fixture) : base(fixture)
        {
            _broadcaster
                .Setup(b => b.BroadcastNotificationAsync(It.IsAny<NotificationDto>()))
                .Callback<NotificationDto>(_pushed.Add)
                .Returns(Task.CompletedTask);
        }

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        private NotificationEventHandler NewSut(IApplicationDbContext context) => new(
            new NotificationService(context, NullLogger<NotificationService>.Instance),
            _broadcaster.Object);

        private static EventContext By(Guid actorId) => new()
        {
            OccurredAt = DateTime.UtcNow,
            Source = EventSource.Request,
            Actor = new EventActor(actorId, "actor", "actor@test.com"),
        };

        /// <summary>
        /// The creator comes from the event, not the actor. Someone else moving the task is
        /// exactly the case the creator wants to hear about.
        /// </summary>
        [Fact]
        public async Task StatusChanged_PushesToTheAssigneeAndTheCreatorFromTheEvent()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).HandleAsync(new TaskStatusChanged
            {
                TaskId = TaskId, GroupId = GroupId, Title = "T", AssigneeId = MemberId, CreatedById = LeadId,
                OldStatusId = 1, OldStatusName = "Not Started", NewStatusId = 2, NewStatusName = "In Progress"
            }, By(OtherLeadId), CancellationToken.None);

            Assert.Equal(
                new[] { LeadId, MemberId }.Order(),
                _pushed.Select(n => n.UserId).Order());
            Assert.All(_pushed, n => Assert.Equal(NotificationType.TaskStatusChanged, n.Type));
        }

        [Fact]
        public async Task PriorityChanged_PushesAPriorityNotificationNotAStatusOne()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).HandleAsync(new TaskPriorityChanged
            {
                TaskId = TaskId, GroupId = GroupId, Title = "T", AssigneeId = MemberId,
                OldPriorityId = 2, OldPriorityName = "Medium", NewPriorityId = 3, NewPriorityName = "High"
            }, By(LeadId), CancellationToken.None);

            var pushed = Assert.Single(_pushed);
            Assert.Equal(MemberId, pushed.UserId);
            Assert.Equal(NotificationType.TaskPriorityChanged, pushed.Type);
        }

        [Fact]
        public async Task CommentAdded_PushesToTheCreatorFromTheEvent()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).HandleAsync(new CommentAdded
            {
                CommentId = Guid.NewGuid(), TaskId = TaskId, GroupId = GroupId, Content = "looks good",
                TaskTitle = "T", TaskAssigneeId = MemberId, TaskCreatedById = LeadId
            }, By(MemberId), CancellationToken.None);

            var pushed = Assert.Single(_pushed);
            Assert.Equal(LeadId, pushed.UserId);
            Assert.Equal(NotificationType.TaskCommentAdded, pushed.Type);
        }

        [Fact]
        public async Task Unassigned_PushesToThePreviousAssignee()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).HandleAsync(new TaskUnassigned
            {
                TaskId = TaskId, GroupId = GroupId, Title = "T",
                PreviousAssigneeId = MemberId, PreviousAssigneeName = "member"
            }, By(LeadId), CancellationToken.None);

            var pushed = Assert.Single(_pushed);
            Assert.Equal(MemberId, pushed.UserId);
        }

        /// <summary>
        /// No row written means nothing to push. A task created without an assignee tells nobody.
        /// </summary>
        [Fact]
        public async Task Created_WithoutAnAssignee_PushesNothing()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).HandleAsync(new TaskCreated
            {
                TaskId = TaskId, GroupId = GroupId, Title = "T", StatusId = 1, AssigneeId = null
            }, By(LeadId), CancellationToken.None);

            Assert.Empty(_pushed);
        }
    }
}
