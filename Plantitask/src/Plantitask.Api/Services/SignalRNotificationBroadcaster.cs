using Microsoft.AspNetCore.SignalR;
using Plantitask.Api.Hubs;
using Plantitask.Api.Interfaces;
using Plantitask.Core.DTO.Notifications;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Services;

/// <summary>
/// Delivers notification DTOs over SignalR after they are committed. Both methods swallow
/// transport errors deliberately - a dropped live push is fine because the notification is
/// already in the database and shows on the next fetch, while failing the request over a
/// broadcast would not be.
/// </summary>
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

    /// <summary>Pushes to the recipient's personal "user_{id}" room - joined only by their own connections.</summary>
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

    /// <summary>Pushes to the membership-gated "group_{id}" room - NotificationHub checks membership at join time.</summary>
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