using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plantitask.Api.Extensions;
using Plantitask.Core.DTO.Notifications;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Controllers;

[Authorize]
[ApiController]
[EnableRateLimiting("general")]
[Route("api/notification-preferences")]
public class NotificationPreferencesController : BaseApiController
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<NotificationPreferencesController> _logger;

    public NotificationPreferencesController(
        INotificationService notificationService,
        ILogger<NotificationPreferencesController> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<NotificationPreferenceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPreferences()
    {
        var userId = GetUserId();
        var result = await _notificationService.GetUserPreferencesAsync(userId);
        return result.ToActionResult();
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SavePreferences([FromBody] UpdateNotificationPreferencesDto dto)
    {
        var userId = GetUserId();
        var result = await _notificationService.SaveUserPreferencesAsync(userId, dto);
        if (result.IsFailure)
            return result.ToActionResult();

        return Ok(new { message = "Notification preferences saved successfully" });
    }
}