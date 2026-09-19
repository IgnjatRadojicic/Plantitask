using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plantitask.Api.Extensions;
using Plantitask.Core.DTO.Auth;
using Plantitask.Core.DTO.Plans;
using Plantitask.Core.DTO.Users;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;

namespace Plantitask.Api.Controllers;

[Authorize]
[ApiController]
[EnableRateLimiting("general")]
[Route("api/user/profile")]
public class UserProfileController : BaseApiController
{
    private readonly IUserProfileService _profileService;
    private readonly IAuthService _authService;
    private readonly IEntitlementService _entitlements;

    public UserProfileController(
        IUserProfileService profileService,
        IAuthService authService,
        IEntitlementService entitlements)
    {
        _profileService = profileService;
        _authService = authService;
        _entitlements = entitlements;
    }

    [HttpGet]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile()
    {
        var userId = GetUserId();
        var result = await _profileService.GetProfileAsync(userId);
        return result.ToActionResult();
    }

    /// <summary>
    /// The caller's plan limits and current usage. The only endpoint that carries policy, so
    /// identity payloads do not have to, and it ships usage next to the limits because a quota
    /// the user cannot see is one that only shows up as a refused upload.
    /// </summary>
    [HttpGet("entitlements")]
    [ProducesResponseType(typeof(EntitlementsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEntitlements()
    {
        var userId = GetUserId();
        var result = await _entitlements.GetUsageAsync(userId);
        return result.ToActionResult();
    }

    [HttpPut]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateUserProfileDto dto)
    {
        var userId = GetUserId();
        var result = await _profileService.UpdateProfileAsync(userId, dto);
        return result.ToActionResult();
    }

    [HttpPost("picture")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    // Above MaxProfilePictureMb (5) so the friendly Result fires instead of a bare 413.
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> UploadProfilePicture(IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        var userId = GetUserId();
        using var stream = file.OpenReadStream();
        var result = await _profileService.UploadProfilePictureAsync(
            userId, stream, file.FileName);

        if (result.IsFailure)
            return result.ToActionResult();

        return Ok(new { path = result.Value });
    }

    [HttpDelete("picture")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveProfilePicture()
    {
        var userId = GetUserId();
        var result = await _profileService.RemoveProfilePictureAsync(userId);

        if (result.IsFailure)
            return result.ToActionResult();

        return Ok(new { message = "Profile picture removed" });
    }

    [HttpPost("change-password")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var result = await _authService.ChangePasswordAsync(GetUserId(), dto, GetClientIpAddress());

        return result.ToActionResult();
    }

}