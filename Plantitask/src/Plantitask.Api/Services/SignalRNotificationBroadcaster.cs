using Microsoft.AspNetCore.SignalR;
using Plantitask.Api.Hubs;
using Plantitask.Api.Interfaces;
using Plantitask.Core.DTO.Notifications;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Services;

public class SignalRNotificationBroadcaster : INotificationBroadcaster
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<SignalRNotificationBroadcaster> _logger;

    public SignalRNotificationBroadcaster(
        IHubContext<NotificationHub> hubContext,
        ILogger<SignalRNotificationBroadcaster> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task BroadcastNotificationAsync(NotificationDto notification)
    {
        try
        {
            await _hubContext.Clients
                .Group($"user_{notification.UserId}")
                .SendAsync("ReceiveNotification", notification);

            _logger.LogInformation(
                "Notification broadcast to user {UserId} via SignalR: {Title}",
                notification.UserId, notification.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error broadcasting notification via SignalR to user {UserId}",
                notification.UserId);
        }
    }

    public async Task BroadcastToGroupAsync(Guid groupId, NotificationDto notification)
    {
        try
        {
            await _hubContext.Clients
                .Group($"group_{groupId}")
                .SendAsync("ReceiveNotification", notification);

            _logger.LogInformation(
                "Notification broadcast to group {GroupId} via SignalR: {Title}",
                groupId, notification.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error broadcasting notification to group {GroupId}",
                groupId);
        }
    }
}