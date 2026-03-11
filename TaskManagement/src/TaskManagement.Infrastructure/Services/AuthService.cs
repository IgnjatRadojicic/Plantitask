using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using TaskManagement.Core.Common;
using TaskManagement.Core.DTO.Auth;
using TaskManagement.Core.Entities;
using TaskManagement.Core.Interfaces;
using TaskManagement.Core.Models;
using Microsoft.Extensions.Configuration;

namespace TaskManagement.Infrastructure.Services
{
    public class AuthService : IAuthService
    {

        private readonly IApplicationDbContext _context;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IJwtTokenGenerator _tokenGenerator;
        private readonly IEmailService _emailService;
        private readonly IRedisService _redisService;
        private readonly ILogger<AuthService> _logger;
        private readonly IConfiguration _configuration;

        public AuthService(
            IApplicationDbContext context,
            IPasswordHasher passwordHasher,
            IJwtTokenGenerator tokenGenerator,
            IEmailService emailService,
            ILogger<AuthService> logger,
            IConfiguration configuration,
            IRedisService redisService)
        {
            _context = context;
            _redisService = redisService;
            _passwordHasher = passwordHasher;
            _tokenGenerator = tokenGenerator;
            _configuration = configuration;
            _emailService = emailService;
            _logger = logger;

        }

        public async Task<AuthResponseDto> RegisterAsync(RegisterDto registerDto)
        {

            _logger.LogInformation("Attempting to register user with email: {Email}", registerDto.Email);

            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == registerDto.Email || u.UserName == registerDto.UserName);

            if (existingUser != null)
            {
                if (existingUser.Email == registerDto.Email)
                    throw new InvalidOperationException("A user with this email already exists");
                throw new InvalidOperationException("A user with username already exists");
            }


            var user = new User
            {
                UserName = registerDto.UserName,
                Email = registerDto.Email,
                PasswordHash = _passwordHasher.HashPassword(registerDto.Password),
                FirstName = registerDto.FirstName,
                LastName = registerDto.LastName,
                IsEmailConfirmed = false,
                CreatedAt = DateTime.UtcNow

            };



            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("New user registered: {Email}", user.Email);

            try
            {
                await _emailService.SendWelcomeEmailAsync(user.Email, user.UserName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send the welcome email");
            }

            var accessToken = _tokenGenerator.GenerateAccessToken(user);
            var refreshToken = _tokenGenerator.GenerateRefreshToken();



            await StoreRefreshTokenAsync(user.Id, refreshToken, "Unknown");

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                UserId = user.Id,
                UserName = user.UserName,
                Email = user.Email
            };
        }

        public async Task<AuthResponseDto> LoginAsync(LoginDto loginDto)
        {
            _logger.LogInformation("Login attempt for email: {Email}", loginDto.Email);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == loginDto.Email);

            if (user == null || !_passwordHasher.VerifyPassword(loginDto.Password, user.PasswordHash))
            {
                _logger.LogWarning("Login attempt with non-existent email: {Email}", loginDto.Email);
                throw new UnauthorizedAccessException("Invalid email or password");
            }

            user.LastLoginAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("User logged in successfully: {Email}", user.Email);



            var accessToken = _tokenGenerator.GenerateAccessToken(user);
            var refreshToken = _tokenGenerator.GenerateRefreshToken();

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                UserId = user.Id,
                UserName = user.UserName,
                Email = user.Email
            };
        }

        public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken)
        {
            _logger.LogInformation("Attempting to refresh token");

            var tokenModel = await _redisService.GetRefreshTokenAsync(refreshToken);

            if (tokenModel == null)
            {
                throw new UnauthorizedAccessException("Invalid refresh token");
            }
            if (tokenModel.IsRevoked)
            {
                throw new UnauthorizedAccessException("Token has been revoked");
            }
            if (tokenModel.ExpiresAt < DateTime.UtcNow)
            {
                throw new UnauthorizedAccessException("Token has  expired");
            }

            var user = await _context.Users.FindAsync(tokenModel.UserId);

            if (user == null)
            {
                throw new UnauthorizedAccessException("User not found");
            }

            var newAccessToken = _tokenGenerator.GenerateAccessToken(user);
            var newRefreshToken = _tokenGenerator.GenerateRefreshToken();

            await _redisService.RevokeRefreshTokenAsync(refreshToken);
            await StoreRefreshTokenAsync(user.Id, newRefreshToken, tokenModel.CreatedByIp);

            _logger.LogInformation("Token refreshed for user: {UserId}", user.Id);

            return new AuthResponseDto
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken,
                UserId = user.Id,
                UserName = user.UserName,
                Email = user.Email
            };
        }

        public async Task LogoutAsync(string refreshToken)
        {
            _logger.LogInformation("User logging out");

            await _redisService.RevokeRefreshTokenAsync(refreshToken);
        }



        public async Task ForgotPasswordAsync(string email)
        {
            _logger.LogInformation("Password reset requested for email: {Email}", email);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                _logger.LogWarning("Password reset requested for non-existent email: {Email}", email);
                return;
            }

            var resetToken = Guid.NewGuid().ToString("N");
            var tokenHash = _passwordHasher.HashPassword(resetToken);

            var passwordResetToken = new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                IsUsed = false,
                IpAddress = "Unknown",
                CreatedAt = DateTime.UtcNow

            };

            _context.PasswordResetTokens.Add(passwordResetToken);
            await _context.SaveChangesAsync();


            var frontendUrl = _configuration["App:FrontendUrl"];
            var resetLink = $"{frontendUrl}/reset-password?token={resetToken}&email={Uri.EscapeDataString(user.Email)}";

            try
            {
                await _emailService.SendPasswordResetEmailAsync(user.Email, user.UserName, resetLink);
                _logger.LogInformation("Password reset email sent to: {Email}", email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email");
            }
        }



        public async Task<Guid> ResetPasswordAsync(ResetPasswordDto resetPasswordDto)
        {
            _logger.LogInformation("Attempting password reset with token");

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == resetPasswordDto.Email);

            if (user == null)
            {
                throw new InvalidOperationException("Invalid or expired reset token");
            }

            var userTokens = await _context.PasswordResetTokens
                .Where(rt => rt.UserId == user.Id && !rt.IsUsed && rt.ExpiresAt > DateTime.UtcNow)
                .OrderByDescending(rt => rt.CreatedAt)
                .ToListAsync();

            PasswordResetToken? resetToken = null;
            foreach (var token in userTokens)
            {
                if (_passwordHasher.VerifyPassword(resetPasswordDto.Token, token.TokenHash))
                {
                    resetToken = token;
                    break;
                }
            }


            if (resetToken == null)
            {
                throw new InvalidOperationException("Invalid or expired reset token");
            }

            user.PasswordHash = _passwordHasher.HashPassword(resetPasswordDto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            resetToken.IsUsed = true;
            resetToken.UsedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Password reset successfully for user: {UserId}", user.Id);

            return user.Id;

        }


        private async Task StoreRefreshTokenAsync(Guid userId, string refreshToken, string ipAddress)
        {
            var tokenModel = new RefreshTokenModel
            {
                UserId = userId,
                TokenHash = _passwordHasher.HashPassword(refreshToken),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(
            int.Parse(_configuration["Jwt:RefreshTokenExpirationDays"] ?? "7")),
                CreatedByIp = ipAddress,
                IsRevoked = false
            };

            var expiration = tokenModel.ExpiresAt - DateTime.UtcNow;
            await _redisService.SetRefreshTokenAsync(refreshToken, tokenModel, expiration);
        }
    }
}
