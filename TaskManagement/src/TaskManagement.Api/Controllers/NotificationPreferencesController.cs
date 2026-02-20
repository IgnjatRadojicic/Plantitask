using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagement.Core.DTO.Notifications;
using TaskManagement.Core.Interfaces;

namespace TaskManagement.Api.Controllers;

[Authorize]
[ApiController]
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
        try
        {
            var userId = GetUserId();
            var preferences = await _notificationService.GetUserPreferencesAsync(userId);
            return Ok(preferences);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notification preferences");
            return StatusCode(500, new { message = "An error occurred while retrieving preferences" });
        }
    }

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SavePreferences([FromBody] UpdateNotificationPreferencesDto dto)
    {
        try
        {
            var userId = GetUserId();
            await _notificationService.SaveUserPreferencesAsync(userId, dto);
            return Ok(new { message = "Notification preferences saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving notification preferences");
            return StatusCode(500, new { message = "An error occurred while saving preferences" });
        }
    }
}