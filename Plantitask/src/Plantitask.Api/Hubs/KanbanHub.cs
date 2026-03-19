using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Plantitask.Api.Hubs
{
    public class KanbanHub : Hub
    {

        public async Task JoinBoard(Guid groupId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"kanban-{groupId}");
        }

        public async Task LeaveBoard(Guid groupId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"kanban-{groupId}");
        }
    }
}
