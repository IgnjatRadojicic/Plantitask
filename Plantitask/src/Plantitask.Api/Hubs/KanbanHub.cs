using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Plantitask.Core.Interfaces;
using System.IdentityModel.Tokens.Jwt;

namespace Plantitask.Api.Hubs
{
    [Authorize]
    public class KanbanHub : Hub
    {
        private readonly IGroupService _groupService;

        public KanbanHub(IGroupService groupService)
        {
            _groupService = groupService;
        }

        public async Task JoinBoard(Guid groupId)
        {
            if (!await _groupService.IsUserMemberAsync(groupId, GetUserId()))
                throw new HubException("You are not a member of this group");

            await Groups.AddToGroupAsync(Context.ConnectionId, $"kanban-{groupId}");
        }

        // Leaving is ungated on purpose. A member removed from a group must still be able to
        // drop the room and nobody is harmed by leaving one they were never in.
        public async Task LeaveBoard(Guid groupId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"kanban-{groupId}");
        }

        private Guid GetUserId()
        {
            var sub = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            return Guid.TryParse(sub, out var userId)
                ? userId
                : throw new HubException("Unauthenticated");
        }
    }
}
