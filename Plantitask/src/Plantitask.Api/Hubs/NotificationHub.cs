using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Plantitask.Core.Interfaces;
using System.IdentityModel.Tokens.Jwt;

namespace Plantitask.Api.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    private readonly IGroupService _groupService;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(IGroupService groupService, ILogger<NotificationHub> logger)
    {
        _groupService = groupService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            _logger.LogInformation("User {UserId} connected to notification hub", userId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            _logger.LogInformation("User {UserId} disconnected from notification hub", userId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinGroupRoom(Guid groupId)
    {
        if (!await _groupService.IsUserMemberAsync(groupId, GetUserId()))
            throw new HubException("You are not a member of this group");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"group_{groupId}");
        _logger.LogInformation("Connection {ConnectionId} joined group {GroupId}",
            Context.ConnectionId, groupId);
    }

    // Leaving is ungated on purpose. A member removed from a group must still be able to
    // drop the room and nobody is harmed by leaving one they were never in.
    public async Task LeaveGroupRoom(Guid groupId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"group_{groupId}");
        _logger.LogInformation("Connection {ConnectionId} left group {GroupId}",
            Context.ConnectionId, groupId);
    }

    private Guid GetUserId()
    {
        var sub = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        return Guid.TryParse(sub, out var userId)
            ? userId
            : throw new HubException("Unauthenticated");
    }
}
