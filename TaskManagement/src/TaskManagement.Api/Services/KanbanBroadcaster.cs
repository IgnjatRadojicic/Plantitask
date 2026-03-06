using Microsoft.AspNetCore.SignalR;
using TaskManagement.Api.Hubs;
using TaskManagement.Core.DTO.Kanban;
using TaskManagement.Core.Interfaces;

namespace TaskManagement.Api.Services
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
    }
}