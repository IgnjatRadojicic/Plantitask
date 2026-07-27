using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plantitask.Api.Extensions;
using Plantitask.Core.DTO.Users;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Controllers;

[Authorize]
[ApiController]
[EnableRateLimiting("general")]
[Route("api/user/profile")]
public class UserProfileController : BaseApiController
{
    private readonly IUserProfileService _profileService;

    public UserProfileController(IUserProfileService profileService)
    {
        _profileService = profileService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile()
    {
        var userId = GetUserId();
        var result = await _profileService.GetProfileAsync(userId);
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

        return Ok(new { url = result.Value });
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
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = GetUserId();
        var result = await _profileService.ChangePasswordAsync(userId, dto);

        if (result.IsFailure)
            return result.ToActionResult();

        return Ok(new { message = "Password changed successfully" });
    }
}