using Moq;
using Plantitask.Contracts.DTO.Kanban;
using Plantitask.Core.Events;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Events.Handlers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Events
{
    public class BroadcastEventHandlerTests
    {
        private readonly Mock<IKanbanBroadcaster> _kanban = new();
        private readonly Mock<ITreeProgressBroadcaster> _tree = new();

        private BroadcastEventHandler NewSut() => new(_kanban.Object, _tree.Object);

        private static EventContext By(Guid actorId) => new()
        {
            OccurredAt = DateTime.UtcNow,
            Source = EventSource.Request,
            Actor = new EventActor(actorId, "actor", "actor@test.com"),
        };

        private static EventContext ByNobody() => new()
        {
            OccurredAt = DateTime.UtcNow,
            Source = EventSource.Job,
            Actor = null,
        };

        private void VerifyNothingElseWasBroadcast()
        {
            _kanban.VerifyNoOtherCalls();
            _tree.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Created_BroadcastsTheNewCardAndTheTree()
        {
            await NewSut().HandleAsync(new TaskCreated
            {
                TaskId = TaskId, GroupId = GroupId, Title = "T", StatusId = 1, AssigneeId = null
            }, By(LeadId), CancellationToken.None);

            _kanban.Verify(k => k.BroadcastTaskCreatedAsync(GroupId, TaskId, 1, LeadId), Times.Once);
            _tree.Verify(t => t.BroadcastTreeUpdateAsync(GroupId), Times.Once);
            VerifyNothingElseWasBroadcast();
        }

        [Fact]
        public async Task Updated_BroadcastsTheCardAndLeavesTheTreeAlone()
        {
            await NewSut().HandleAsync(new TaskUpdated
            {
                TaskId = TaskId, GroupId = GroupId, Title = "T", AssigneeId = MemberId
            }, By(LeadId), CancellationToken.None);

            _kanban.Verify(k => k.BroadcastTaskUpdatedAsync(GroupId, TaskId, LeadId), Times.Once);
            VerifyNothingElseWasBroadcast();
        }

        [Fact]
        public async Task PriorityChanged_BroadcastsTheCardAndLeavesTheTreeAlone()
        {
            await NewSut().HandleAsync(new TaskPriorityChanged
            {
                TaskId = TaskId, GroupId = GroupId, Title = "T", AssigneeId = MemberId,
                OldPriorityId = 2, OldPriorityName = "Medium", NewPriorityId = 3, NewPriorityName = "High"
            }, By(LeadId), CancellationToken.None);

            _kanban.Verify(k => k.BroadcastTaskUpdatedAsync(GroupId, TaskId, LeadId), Times.Once);
            VerifyNothingElseWasBroadcast();
        }

        /// <summary>
        /// A status change is an update on the board, not a move. The tree redraws because the
        /// status is what its progress is counted from.
        /// </summary>
        [Fact]
        public async Task StatusChanged_BroadcastsTheCardAsUpdatedAndTheTree()
        {
            await NewSut().HandleAsync(new TaskStatusChanged
            {
                TaskId = TaskId, GroupId = GroupId, Title = "T", AssigneeId = MemberId, CreatedById = LeadId,
                OldStatusId = 1, OldStatusName = "Not Started", NewStatusId = 2, NewStatusName = "In Progress"
            }, By(LeadId), CancellationToken.None);

            _kanban.Verify(k => k.BroadcastTaskUpdatedAsync(GroupId, TaskId, LeadId), Times.Once);
            _tree.Verify(t => t.BroadcastTreeUpdateAsync(GroupId), Times.Once);
            VerifyNothingElseWasBroadcast();
        }

        [Fact]
        public async Task Deleted_BroadcastsTheColumnTheCardLeftAndTheTree()
        {
            await NewSut().HandleAsync(new TaskDeleted
            {
                TaskId = TaskId, GroupId = GroupId, Title = "T", StatusId = 2
            }, By(LeadId), CancellationToken.None);

            _kanban.Verify(k => k.BroadcastTaskDeletedAsync(GroupId, TaskId, 2, LeadId), Times.Once);
            _tree.Verify(t => t.BroadcastTreeUpdateAsync(GroupId), Times.Once);
            VerifyNothingElseWasBroadcast();
        }

        [Fact]
        public async Task Moved_ToAnotherColumn_BroadcastsTheMoveAndTheTree()
        {
            await NewSut().HandleAsync(new TaskMoved
            {
                TaskId = TaskId, GroupId = GroupId,
                OldStatusId = 1, OldStatusName = "Not Started",
                NewStatusId = 3, NewStatusName = "Completed",
                NewDisplayOrder = 4
            }, By(LeadId), CancellationToken.None);

            _kanban.Verify(k => k.BroadcastTaskMovedAsync(
                GroupId, TaskId, 1,
                It.Is<MoveTaskDto>(m => m.NewStatusId == 3 && m.NewDisplayOrder == 4),
                LeadId), Times.Once);
            _tree.Verify(t => t.BroadcastTreeUpdateAsync(GroupId), Times.Once);
            VerifyNothingElseWasBroadcast();
        }

        /// <summary>
        /// Reordering inside one column changes no status, so the tree has nothing new to count.
        /// </summary>
        [Fact]
        public async Task Moved_WithinTheSameColumn_BroadcastsTheMoveAndLeavesTheTreeAlone()
        {
            await NewSut().HandleAsync(new TaskMoved
            {
                TaskId = TaskId, GroupId = GroupId,
                OldStatusId = 2, OldStatusName = "In Progress",
                NewStatusId = 2, NewStatusName = "In Progress",
                NewDisplayOrder = 0
            }, By(LeadId), CancellationToken.None);

            _kanban.Verify(k => k.BroadcastTaskMovedAsync(
                GroupId, TaskId, 2,
                It.Is<MoveTaskDto>(m => m.NewStatusId == 2 && m.NewDisplayOrder == 0),
                LeadId), Times.Once);
            VerifyNothingElseWasBroadcast();
        }

        /// <summary>
        /// The payload names who made the change. A job or a webhook has no actor, and Guid.Empty
        /// is an id no real user has.
        /// </summary>
        [Fact]
        public async Task NoActor_BroadcastsWithAnEmptyUserId()
        {
            await NewSut().HandleAsync(new TaskUpdated
            {
                TaskId = TaskId, GroupId = GroupId, Title = "T", AssigneeId = null
            }, ByNobody(), CancellationToken.None);

            _kanban.Verify(k => k.BroadcastTaskUpdatedAsync(GroupId, TaskId, Guid.Empty), Times.Once);
            VerifyNothingElseWasBroadcast();
        }
    }
}
