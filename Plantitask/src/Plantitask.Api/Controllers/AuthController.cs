using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plantitask.Api.Extensions;
using Plantitask.Core.DTO.Auth;
using Plantitask.Core.Interfaces;
using Microsoft.AspNetCore.RateLimiting;

namespace Plantitask.Api.Controllers
{
    [ApiController]
    [EnableRateLimiting("auth")]
    [Route("api/[controller]")]
    public class AuthController : BaseApiController
    {
        private readonly IAuthService _authService;
        private readonly IAuditService _auditService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IAuthService authService,
            IAuditService auditService,
            ILogger<AuthController> logger)
        {
            _authService = authService;
            _auditService = auditService;
            _logger = logger;
        }

        [HttpPost("register")]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            var result = await _authService.RegisterAsync(registerDto);

            if (result.IsFailure)
                return result.ToActionResult();

            var response = result.Value!;

            await _auditService.LogAsync(
                entityType: "User",
                entityId: response.UserId,
                action: "Registered",
                userId: response.UserId,
                groupId: null,
                ipAddress: GetClientIpAddress(),
                userAgent: GetUserAgent());

            return Ok(response);
        }

        [HttpPost("login")]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            var result = await _authService.LoginAsync(loginDto);

            if (result.IsFailure)
                return result.ToActionResult();

            var response = result.Value!;

            await _auditService.LogAsync(
                entityType: "User",
                entityId: response.UserId,
                action: "Login",
                userId: response.UserId,
                groupId: null,
                ipAddress: GetClientIpAddress(),
                userAgent: GetUserAgent());

            return Ok(response);
        }

        [HttpPost("refresh")]
        [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto refreshTokenDto)
        {
            var result = await _authService.RefreshTokenAsync(refreshTokenDto.RefreshToken);
            return result.ToActionResult();
        }

        [Authorize]
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenDto refreshTokenDto)
        {
            var userId = GetUserId();

            var result = await _authService.LogoutAsync(refreshTokenDto.RefreshToken);
            if (result.IsFailure)
                return result.ToActionResult();

            await LogAuditAsync(
                _auditService,
                entityType: "User",
                entityId: userId,
                action: "Logout",
                groupId: null);

            return Ok(new { message = "Logged out successfully" });
        }

        [HttpPost("forgot-password")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto forgotPasswordDto)
        {
            var result = await _authService.ForgotPasswordAsync(forgotPasswordDto.Email);
            if (result.IsFailure)
                return result.ToActionResult();

            return Ok(new { message = "Password reset email sent" });
        }

        [HttpPost("reset-password")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto resetPasswordDto)
        {
            var result = await _authService.ResetPasswordAsync(resetPasswordDto);

            if (result.IsFailure)
                return result.ToActionResult();

            var userId = result.Value!;

            await _auditService.LogAsync(
                entityType: "User",
                entityId: userId,
                action: "PasswordReset",
                userId: userId,
                groupId: null,
                ipAddress: GetClientIpAddress(),
                userAgent: GetUserAgent());

            return Ok(new { message = "Password reset successfully" });
        }

        [HttpPost("check-email")]
        [EnableRateLimiting("verification")]
        public async Task<IActionResult> CheckEmail([FromBody] CheckEmailDto dto)
        {
            var result = await _authService.CheckEmailAsync(dto.Email);
            return result.ToActionResult();
        }

        [HttpPost("send-verification")]

        public async Task<IActionResult> SendVerification([FromBody] SendVerificationRequest dto)
        {
            var result = await _authService.SendVerificationCodeAsync(dto.Email);
            if (result.IsFailure)
                return result.ToActionResult();

            return Ok(new { message = "Verification code sent" });
        }

        [HttpPost("verify-email")]
        [EnableRateLimiting("verification")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto dto)
        {
            var result = await _authService.VerifyEmailCodeAsync(dto.Email, dto.Code);
            if (result.IsFailure)
                return result.ToActionResult();

            return Ok(new { message = "Email verified successfully" });
        }

        [HttpPost("google-login")]

        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginDto dto)
        {
            var result = await _authService.GoogleLoginAsync(dto);
            return result.ToActionResult();
        }
    }
}