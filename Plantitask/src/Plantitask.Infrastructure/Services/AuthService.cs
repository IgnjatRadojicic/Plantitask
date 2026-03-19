using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Auth;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Models;
using Microsoft.Extensions.Configuration;
using Google.Apis.Auth;

namespace Plantitask.Infrastructure.Services
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
        private readonly GoogleAuthSettings _googleSettings;

        public AuthService(
            IApplicationDbContext context,
            IPasswordHasher passwordHasher,
            IJwtTokenGenerator tokenGenerator,
            IEmailService emailService,
            ILogger<AuthService> logger,
            IConfiguration configuration,
            IRedisService redisService,
            IOptions<GoogleAuthSettings> googleSettings)
        {
            _context = context;
            _redisService = redisService;
            _passwordHasher = passwordHasher;
            _tokenGenerator = tokenGenerator;
            _configuration = configuration;
            _emailService = emailService;
            _logger = logger;
            _googleSettings = googleSettings.Value;
        }

        public async Task<Result<AuthResponseDto>> RegisterAsync(RegisterDto registerDto)
        {
            _logger.LogInformation("Attempting to register user with email: {Email}", registerDto.Email);

            var isVerified = await _redisService.IsEmailVerifiedAsync(registerDto.Email);
            if (!isVerified)
                return Error.BadRequest("Email must be verified before registration");

            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == registerDto.Email || u.UserName == registerDto.UserName);

            if (existingUser != null)
            {
                if (existingUser.Email == registerDto.Email)
                    return Error.Conflict("A user with this email already exists");
                return Error.Conflict("A user with this username already exists");
            }

            var user = new User
            {
                UserName = registerDto.UserName,
                Email = registerDto.Email,
                PasswordHash = _passwordHasher.HashPassword(registerDto.Password),
                FirstName = registerDto.FirstName,
                LastName = registerDto.LastName,
                IsEmailConfirmed = true,
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
                _logger.LogError(ex, "Failed to send welcome email");
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

        public async Task<Result<AuthResponseDto>> LoginAsync(LoginDto loginDto)
        {
            _logger.LogInformation("Login attempt for email: {Email}", loginDto.Email);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == loginDto.Email);

            if (user == null || !_passwordHasher.VerifyPassword(loginDto.Password, user.PasswordHash))
                return Error.Forbidden("Invalid email or password");

            if (!user.IsEmailConfirmed)
                return Error.BadRequest("Please verify your email before logging in. Check your inbox for the verification code.");

            user.LastLoginAt = DateTime.UtcNow;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("User logged in successfully: {Email}", user.Email);

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

        public async Task<Result<AuthResponseDto>> RefreshTokenAsync(string refreshToken)
        {
            _logger.LogInformation("Attempting to refresh token");

            var tokenModel = await _redisService.GetRefreshTokenAsync(refreshToken);

            if (tokenModel == null)
                return Error.Forbidden("Invalid refresh token");
            if (tokenModel.IsRevoked)
                return Error.Forbidden("Token has been revoked");
            if (tokenModel.ExpiresAt < DateTime.UtcNow)
                return Error.Forbidden("Token has expired");

            var user = await _context.Users.FindAsync(tokenModel.UserId);
            if (user == null)
                return Error.Forbidden("User not found");

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

        public async Task<Result> LogoutAsync(string refreshToken)
        {
            _logger.LogInformation("User logging out");
            await _redisService.RevokeRefreshTokenAsync(refreshToken);
            return Result.Success();
        }

        public async Task<Result<CheckEmailResponseDto>> CheckEmailAsync(string email)
        {
            var exists = await _context.Users
                .AnyAsync(u => u.Email == email);
            return new CheckEmailResponseDto { Exists = exists };
        }

        public async Task<Result> SendVerificationCodeAsync(string email)
        {
            var createdAt = await _redisService.GetVerificationCodeCreatedAtAsync(email);

            if (createdAt.HasValue && createdAt.Value > DateTime.UtcNow.AddMinutes(-1))
                return Error.BadRequest("Please wait at least 1 minute before requesting a new code");

            var code = GenerateVerificationCode();
            var codeHash = _passwordHasher.HashPassword(code);

            await _redisService.StoreVerificationCodeAsync(email, codeHash, TimeSpan.FromMinutes(15));

            var userName = email.Split('@')[0];
            await _emailService.SendEmailVerificationCodeAsync(email, userName, code);

            _logger.LogInformation("Verification code sent to {Email}", email);

            return Result.Success();
        }

        public async Task<Result> VerifyEmailCodeAsync(string email, string code)
        {
            var codeHash = await _redisService.GetVerificationCodeHashAsync(email);

            if (codeHash == null)
                return Error.BadRequest("Invalid or expired verification code");

            if (!_passwordHasher.VerifyPassword(code, codeHash))
                return Error.BadRequest("Invalid or expired verification code");

            await _redisService.MarkVerificationCodeUsedAsync(email);

            _logger.LogInformation("Email {Email} verified successfully", email);

            return Result.Success();
        }

        public async Task<Result> ForgotPasswordAsync(string email)
        {
            _logger.LogInformation("Password reset requested for email: {Email}", email);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                _logger.LogWarning("Password reset requested for non-existent email: {Email}", email);
                return Result.Success();
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

            return Result.Success();
        }

        public async Task<Result<Guid>> ResetPasswordAsync(ResetPasswordDto resetPasswordDto)
        {
            _logger.LogInformation("Attempting password reset");

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == resetPasswordDto.Email);

            if (user == null)
                return Error.BadRequest("Invalid or expired reset token");

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
                return Error.BadRequest("Invalid or expired reset token");

            user.PasswordHash = _passwordHasher.HashPassword(resetPasswordDto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            resetToken.IsUsed = true;
            resetToken.UsedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Password reset successfully for user: {UserId}", user.Id);

            return user.Id;
        }

        public async Task<Result<AuthResponseDto>> GoogleLoginAsync(GoogleLoginDto dto)
        {
            _logger.LogInformation("Google login attempt");

            GoogleJsonWebSignature.Payload payload;

            try
            {
                var settings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _googleSettings.ClientId }
                };

                payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, settings);
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogWarning(ex, "Invalid Google token");
                return Error.Forbidden("Invalid Google token");
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == payload.Email);

            if (user == null)
            {
                user = new User
                {
                    UserName = GenerateUsernameFromEmail(payload.Email),
                    Email = payload.Email,
                    PasswordHash = _passwordHasher.HashPassword(Guid.NewGuid().ToString()),
                    FirstName = payload.GivenName,
                    LastName = payload.FamilyName,
                    ProfilePictureUrl = payload.Picture,
                    IsEmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation("New user created via Google SSO: {Email}", user.Email);

                try
                {
                    await _emailService.SendWelcomeEmailAsync(user.Email, user.UserName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send welcome email for Google user");
                }
            }
            else
            {
                if (!user.IsEmailConfirmed)
                    user.IsEmailConfirmed = true;

                user.LastLoginAt = DateTime.UtcNow;
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Existing user logged in via Google: {Email}", user.Email);
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

        private static string GenerateVerificationCode()
        {
            return RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        }

        private static string GenerateUsernameFromEmail(string email)
        {
            var baseName = email.Split('@')[0]
                .Replace(".", "_")
                .Replace("-", "_");

            var suffix = RandomNumberGenerator.GetInt32(1000, 9999);
            return $"{baseName}_{suffix}";
        }
    }
}