using Microsoft.AspNetCore.SignalR;
using Plantitask.Api.Hubs;
using Plantitask.Core.DTO.Kanban;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Services
{
    /// <summary>
    /// Pushes board changes into the "kanban-{groupId}" SignalR room. No per-user checks here
    /// on purpose - KanbanHub verifies membership before anyone joins the room, so the room
    /// itself is the tenant boundary. Every event carries the acting user's id so clients can
    /// ignore their own echoes.
    /// </summary>
    public class KanbanBroadcaster : IKanbanBroadcaster
    {
        private readonly IHubContext<KanbanHub> _hubContext;

        public KanbanBroadcaster(IHubContext<KanbanHub> hubContext)
        {
            _hubContext = hubContext;
        }

        /// <summary>A drag landed: old and new column plus the new position, typed as a shared event.</summary>
        public async Task BroadcastTaskMovedAsync(Guid groupId, Guid taskId, int oldStatusId, MoveTaskDto moveDto, Guid movedByUserId)
        {
            await _hubContext.Clients
                .Group($"kanban-{groupId}")
                .SendAsync("TaskMoved", new KanbanTaskMovedEvent
                {
                    TaskId = taskId,
                    OldStatusId = oldStatusId,
                    NewStatusId = moveDto.NewStatusId,
                    NewDisplayOrder = moveDto.NewDisplayOrder,
                    MovedByUserId = movedByUserId
                });
        }

        /// <summary>A new card appeared in a column; clients fetch the details themselves.</summary>
        public async Task BroadcastTaskCreatedAsync(Guid groupId, Guid taskId, int statusId, Guid createdByUserId)
        {
            await _hubContext.Clients
                .Group($"kanban-{groupId}")
                .SendAsync("TaskCreated", new KanbanTaskCreatedEvent
                {
                    TaskId = taskId,
                    StatusId = statusId,
                    CreatedByUserId = createdByUserId
                });
        }

        /// <summary>A card left the board, with the column it vacated so clients can close the gap.</summary>
        public async Task BroadcastTaskDeletedAsync(Guid groupId, Guid taskId, int statusId, Guid deletedByUserId)
        {
            await _hubContext.Clients
                .Group($"kanban-{groupId}")
                .SendAsync("TaskDeleted", new KanbanTaskDeletedEvent
                {
                    TaskId = taskId,
                    StatusId = statusId,
                    DeletedByUserId = deletedByUserId
                });
        }

        /// <summary>A card's contents changed; carries only the id so clients re-read what they show.</summary>
        public async Task BroadcastTaskUpdatedAsync(Guid groupId, Guid taskId, Guid updatedByUserId)
        {
            await _hubContext.Clients
                .Group($"kanban-{groupId}")
                .SendAsync("TaskUpdated", new KanbanTaskUpdatedEvent
                {
                    TaskId = taskId,
                    UpdatedByUserId = updatedByUserId
                });
        }
    }
}