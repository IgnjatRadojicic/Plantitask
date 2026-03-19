using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Plantitask.Api.Extensions;
using Plantitask.Api.Hubs;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Notifications;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Controllers;

[Authorize]
[ApiController]
[EnableRateLimiting("general")]
[Route("api/[controller]")]
public class NotificationsController : BaseApiController
{
    private readonly INotificationService _notificationService;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationService notificationService,
        IHubContext<NotificationHub> hubContext,
        ILogger<NotificationsController> logger)
    {
        _notificationService = notificationService;
        _hubContext = hubContext;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedList<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        var result = await _notificationService.GetUserNotificationsAsync(userId, unreadOnly, pageNumber, pageSize);
        return result.ToActionResult();
    }

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadCountDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCount()
    {
        var userId = GetUserId();
        var result = await _notificationService.GetUnreadCountAsync(userId);
        return result.ToActionResult();
    }

    [HttpPatch("{notificationId}/read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkAsRead(Guid notificationId)
    {
        var userId = GetUserId();
        var result = await _notificationService.MarkAsReadAsync(notificationId, userId);
        if (result.IsFailure)
            return result.ToActionResult();

        return Ok(new { message = "Notification marked as read" });
    }

    [HttpPut("read-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = GetUserId();
        var result = await _notificationService.MarkAllAsReadAsync(userId);
        if (result.IsFailure)
            return result.ToActionResult();

        return Ok(new { message = "All notifications marked as read" });
    }

    [HttpDelete("{notificationId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteNotification(Guid notificationId)
    {
        var userId = GetUserId();
        var result = await _notificationService.DeleteNotificationAsync(notificationId, userId);
        if (result.IsFailure)
            return result.ToActionResult();

        return Ok(new { message = "Notification deleted" });
    }
}