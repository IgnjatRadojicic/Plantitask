using Microsoft.AspNetCore.SignalR;
using Plantitask.Api.Hubs;
using Plantitask.Core.DTO.Kanban;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Services
{
    public class KanbanBroadcaster : IKanbanBroadcaster
    {
        private readonly IHubContext<KanbanHub> _hubContext;

        public KanbanBroadcaster(IHubContext<KanbanHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task BroadcastTaskMovedAsync(Guid groupId, Guid taskId, int oldStatusId, MoveTaskDto moveDto, Guid movedByUserId)
        {
            await _hubContext.Clients
                .Group($"kanban-{groupId}")
                .SendAsync("TaskMoved", new
                {
                    TaskId = taskId,
                    OldStatusId = oldStatusId,
                    NewStatusId = moveDto.NewStatusId,
                    NewDisplayOrder = moveDto.NewDisplayOrder,
                    MovedByUserId = movedByUserId
                });
        }

        public async Task BroadcastTaskCreatedAsync(Guid groupId, Guid taskId, int statusId, Guid createdByUserId)
        {
            await _hubContext.Clients
                .Group($"kanban-{groupId}")
                .SendAsync("TaskCreated", new
                {
                    TaskId = taskId,
                    StatusId = statusId,
                    CreatedByUserId = createdByUserId
                });
        }

        public async Task BroadcastTaskDeletedAsync(Guid groupId, Guid taskId, int statusId, Guid deletedByUserId)
        {
            await _hubContext.Clients
                .Group($"kanban-{groupId}")
                .SendAsync("TaskDeleted", new
                {
                    TaskId = taskId,
                    StatusId = statusId,
                    DeletedByUserId = deletedByUserId
                });
        }

        public async Task BroadcastTaskUpdatedAsync(Guid groupId, Guid taskId, Guid updatedByUserId)
        {
            await _hubContext.Clients
                .Group($"kanban-{groupId}")
                .SendAsync("TaskUpdated", new
                {
                    TaskId = taskId,
                    UpdatedByUserId = updatedByUserId
                });
        }
    }
}