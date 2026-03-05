using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagement.Core.DTO.Auth;
using TaskManagement.Core.Interfaces;

namespace TaskManagement.Api.Controllers
{
    [ApiController]
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
                var response = await _authService.RegisterAsync(registerDto);

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

                var response = await _authService.LoginAsync(loginDto);

              
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
                var response = await _authService.RefreshTokenAsync(refreshTokenDto.RefreshToken);
                return Ok(response);
        }

        [Authorize]
        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenDto refreshTokenDto)
        {
                var userId = GetUserId();
                await _authService.LogoutAsync(refreshTokenDto.RefreshToken);

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
                await _authService.ForgotPasswordAsync(forgotPasswordDto.Email);
                return Ok(new { message = "Password reset email sent" });
        }


        [HttpPost("reset-password")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto resetPasswordDto)
        {
                var userId = await _authService.ResetPasswordAsync(resetPasswordDto);

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
    }
}
