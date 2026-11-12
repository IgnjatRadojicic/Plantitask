using Plantitask.Contracts.DTO.Kanban;
using Plantitask.Core.Events;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Events.Handlers
{
    public class BroadcastEventHandler :
        IEventHandler<TaskCreated>,
        IEventHandler<TaskUpdated>,
        IEventHandler<TaskStatusChanged>,
        IEventHandler<TaskPriorityChanged>,
        IEventHandler<TaskDeleted>,
        IEventHandler<TaskMoved>
    {
        private readonly IKanbanBroadcaster _kanban;
        private readonly ITreeProgressBroadcaster _tree;

        public BroadcastEventHandler(IKanbanBroadcaster kanban, ITreeProgressBroadcaster tree)
        {
            _kanban = kanban;
            _tree = tree;
        }

        public async Task HandleAsync(TaskCreated e, EventContext context, CancellationToken ct)
        {
            await _kanban.BroadcastTaskCreatedAsync(e.GroupId, e.TaskId, e.StatusId, ActorId(context));
            await _tree.BroadcastTreeUpdateAsync(e.GroupId);
        }

        public Task HandleAsync(TaskUpdated e, EventContext context, CancellationToken ct) =>
            _kanban.BroadcastTaskUpdatedAsync(e.GroupId, e.TaskId, ActorId(context));

        public async Task HandleAsync(TaskStatusChanged e, EventContext context, CancellationToken ct)
        {
            await _kanban.BroadcastTaskUpdatedAsync(e.GroupId, e.TaskId, ActorId(context));
            await _tree.BroadcastTreeUpdateAsync(e.GroupId);
        }

        public Task HandleAsync(TaskPriorityChanged e, EventContext context, CancellationToken ct) =>
            _kanban.BroadcastTaskUpdatedAsync(e.GroupId, e.TaskId, ActorId(context));

        public async Task HandleAsync(TaskDeleted e, EventContext context, CancellationToken ct)
        {
            await _kanban.BroadcastTaskDeletedAsync(e.GroupId, e.TaskId, e.StatusId, ActorId(context));
            await _tree.BroadcastTreeUpdateAsync(e.GroupId);
        }

        public async Task HandleAsync(TaskMoved e, EventContext context, CancellationToken ct)
        {
            var move = new MoveTaskDto { NewStatusId = e.NewStatusId, NewDisplayOrder = e.NewDisplayOrder };
            await _kanban.BroadcastTaskMovedAsync(e.GroupId, e.TaskId, e.OldStatusId, move, ActorId(context));

            if (e.OldStatusId != e.NewStatusId)
                await _tree.BroadcastTreeUpdateAsync(e.GroupId);
        }


        private static Guid ActorId(EventContext context) => context.Actor?.Id ?? Guid.Empty;
    }
}