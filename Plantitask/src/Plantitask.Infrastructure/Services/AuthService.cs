using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Auth;
using Plantitask.Core.DTO.Users;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Models;
using Plantitask.Infrastructure.Security;
using System.Net;
using System.Security.Cryptography;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// Registration, login, token lifecycle and the password flows. Two rules run through the
    /// whole file: failure responses never reveal whether an account exists, and any credential
    /// change revokes every refresh token the user has.
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly IApplicationDbContext _context;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IJwtTokenGenerator _tokenGenerator;
        private readonly IEmailService _emailService;
        private readonly IRedisService _redisService;
        private readonly ILogger<AuthService> _logger;
        private readonly GoogleAuthSettings _googleSettings;
        private readonly JwtSettings _jwtSettings;
        private readonly AppSettings _appSettings;

        public AuthService(
            IApplicationDbContext context,
            IPasswordHasher passwordHasher,
            IJwtTokenGenerator tokenGenerator,
            IEmailService emailService,
            ILogger<AuthService> logger,
            IRedisService redisService,
            IOptions<GoogleAuthSettings> googleSettings,
            IOptions<JwtSettings> jwtSettings,
            IOptions<AppSettings> appSettings)
        {
            _context = context;
            _redisService = redisService;
            _passwordHasher = passwordHasher;
            _tokenGenerator = tokenGenerator;
            _emailService = emailService;
            _logger = logger;
            _googleSettings = googleSettings.Value;
            _jwtSettings = jwtSettings.Value;
            _appSettings = appSettings.Value;
        }

        /// <summary>
        /// Creates an account for an email that already passed code verification, then sends the
        /// welcome email and signs the new user in. Duplicate email and username each get their
        /// own conflict message - registration is where that fact is public by design.
        /// </summary>
        public async Task<Result<AuthResponseDto>> RegisterAsync(RegisterDto registerDto, string ipAddress)
        {
            var email = NormalizeEmail(registerDto.Email);
            var userName = registerDto.UserName.Trim();

            _logger.LogInformation("Attempting to register user with email: {Email}", email);

            var isVerified = await _redisService.IsEmailVerifiedAsync(email);
            if (!isVerified)
                return Error.BadRequest("Email must be verified before registration");

            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email || u.UserName == userName);

            if (existingUser != null)
            {
                if (existingUser.Email == email)
                    return Error.Conflict("A user with this email already exists");
                return Error.Conflict("A user with this username already exists");
            }

            var user = new User
            {
                UserName = userName,
                Email = email,
                PasswordHash = _passwordHasher.HashPassword(registerDto.Password),
                FirstName = registerDto.FirstName,
                LastName = registerDto.LastName,
                IsEmailConfirmed = true,

            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("New user registered: {Email}", user.Email);

            try
            {
                await _emailService.SendWelcomeEmailAsync(user.Email, user.FirstName ?? user.UserName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send welcome email");
            }

            return await GenerateAuthResponseAsync(user, ipAddress);
        }

        /// <summary>
        /// Verifies credentials and issues a token pair. Unknown email, wrong password and
        /// unconfirmed account all return the same message, and a dummy hash is verified when
        /// the user is missing so response timing does not reveal which case it was. Also
        /// rehashes the password on login when the stored work factor is outdated.
        /// </summary>
        public async Task<Result<AuthResponseDto>> LoginAsync(LoginDto loginDto, string ipAddress)
        {
            var email = NormalizeEmail(loginDto.Email);

            _logger.LogInformation("Login attempt for email: {Email}", email);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                _passwordHasher.VerifyPassword(loginDto.Password, _passwordHasher.DummyHash);
                return Error.Unauthorized("Invalid email or password");
            }

            if (!_passwordHasher.VerifyPassword(loginDto.Password, user.PasswordHash))
                return Error.Unauthorized("Invalid email or password");

            if (!user.IsEmailConfirmed)
                return Error.Unauthorized("Invalid email or password");

            if (_passwordHasher.NeedsRehash(user.PasswordHash))
                user.PasswordHash = _passwordHasher.HashPassword(loginDto.Password);

            user.LastLoginAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("User logged in successfully: {Email}", user.Email);

            return await GenerateAuthResponseAsync(user, ipAddress);
        }

        /// <summary>
        /// Rotates a refresh token: the old one is marked revoked (not deleted) and a new pair
        /// is issued. A token that arrives already revoked means someone replayed a used token -
        /// theft's signature - so every session the user has is revoked on the spot.
        /// </summary>
        public async Task<Result<AuthResponseDto>> RefreshTokenAsync(string refreshToken)
        {
            _logger.LogInformation("Attempting to refresh token");

            var tokenModel = await _redisService.GetRefreshTokenAsync(TokenHasher.Sha256(refreshToken));

            if (tokenModel == null)
                return Error.Unauthorized("Invalid refresh token");
            if (tokenModel.IsRevoked)
            {
                _logger.LogWarning("Refresh token reuse detected for user {UserId} — revoking all sessions", tokenModel.UserId);
                await _redisService.RevokeAllUserTokensAsync(tokenModel.UserId);
                return Error.Unauthorized("Invalid refresh token");
            }
            if (tokenModel.ExpiresAt < DateTime.UtcNow)
                return Error.Unauthorized("Invalid refresh token");

            var user = await _context.Users.FindAsync(tokenModel.UserId);
            if (user == null)
                return Error.Unauthorized("Invalid refresh token");

            var newAccessToken = _tokenGenerator.GenerateAccessToken(user);
            var newRefreshToken = _tokenGenerator.GenerateRefreshToken();

            await _redisService.MarkRefreshTokenRevokedAsync(TokenHasher.Sha256(refreshToken));
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

        /// <summary>
        /// Deletes the caller's refresh token. Deletion is right here - marking revoked is for
        /// rotation - and a token owned by someone else is silently ignored so logout can never
        /// be used to probe other users' sessions.
        /// </summary>
        public async Task<Result> LogoutAsync(Guid userId, string refreshToken)
        {
            _logger.LogInformation("User {UserId} logging out", userId);

            var tokenHash = TokenHasher.Sha256(refreshToken);
            var tokenModel = await _redisService.GetRefreshTokenAsync(tokenHash);

            if (tokenModel is not null && tokenModel.UserId != userId)
            {
                _logger.LogWarning("User {UserId} tried to log out a token owned by {OwnerId}",
                    userId, tokenModel.UserId);
                return Result.Success();
            }

            await _redisService.DeleteRefreshTokenAsync(tokenHash);
            return Result.Success();
        }

        /// <summary>
        /// Tells the signup form whether an email is taken. A deliberate existence oracle - the
        /// register flow leaks the same fact - with the per-IP rate limiter as the thing that
        /// makes harvesting expensive. Decision recorded in auth-service.md.
        /// </summary>
        public async Task<Result<CheckEmailResponseDto>> CheckEmailAsync(string email)
        {
            var normalized = NormalizeEmail(email);

            var exists = await _context.Users
                .AnyAsync(u => u.Email == normalized);
            return new CheckEmailResponseDto { Exists = exists };
        }

        /// <summary>
        /// Generates a six-digit code, stores its BCrypt hash in Redis for 15 minutes and emails
        /// it. BCrypt because a short code is guessable, unlike the high-entropy tokens that get
        /// plain SHA-256. Requests inside one minute of the last are refused, and a failed email
        /// send is a real error since the caller cannot continue without the code.
        /// </summary>
        public async Task<Result> SendVerificationCodeAsync(string email)
        {
            email = NormalizeEmail(email);

            var createdAt = await _redisService.GetVerificationCodeCreatedAtAsync(email);

            if (createdAt.HasValue && createdAt.Value > DateTime.UtcNow.AddMinutes(-1))
                return Error.BadRequest("Please wait at least 1 minute before requesting a new code");

            var code = GenerateVerificationCode();
            var codeHash = _passwordHasher.HashPassword(code);

            await _redisService.StoreVerificationCodeAsync(email, codeHash, TimeSpan.FromMinutes(15));

            var userName = email.Split('@')[0];

            try
            {
                await _emailService.SendEmailVerificationCodeAsync(email, userName, code);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send verification code to {Email}", email);
                return Error.Internal("Could not send the verification email. Please try again");
            }

            _logger.LogInformation("Verification code sent to {Email}", email);

            return Result.Success();
        }

        /// <summary>
        /// Checks a submitted code against the stored hash and marks the email verified.
        /// Missing, expired and wrong codes all come back as the same message.
        /// </summary>
        public async Task<Result> VerifyEmailCodeAsync(string email, string code)
        {
            email = NormalizeEmail(email);

            var codeHash = await _redisService.GetVerificationCodeHashAsync(email);

            if (codeHash == null)
                return Error.BadRequest("Invalid or expired verification code");

            if (!_passwordHasher.VerifyPassword(code, codeHash))
                return Error.BadRequest("Invalid or expired verification code");

            await _redisService.MarkVerificationCodeUsedAsync(email);

            _logger.LogInformation("Email {Email} verified successfully", email);

            return Result.Success();
        }

        /// <summary>
        /// Issues a one-hour reset token and emails the link. Always reports success, because an
        /// unknown email must look identical to a known one from outside. Only the SHA-256 hash
        /// of the token is stored - the plaintext exists solely inside the emailed link.
        /// </summary>
        public async Task<Result> ForgotPasswordAsync(string email, string ipAddress)
        {
            email = NormalizeEmail(email);

            _logger.LogInformation("Password reset requested for email: {Email}", email);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                _logger.LogWarning("Password reset requested for non-existent email: {Email}", email);
                return Result.Success();
            }

            var resetToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var tokenHash = TokenHasher.Sha256(resetToken);

            var passwordResetToken = new PasswordResetToken
            {
                UserId = user.Id,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                IsUsed = false,
                IpAddress = ipAddress,

            };

            _context.PasswordResetTokens.Add(passwordResetToken);
            await _context.SaveChangesAsync();

            var resetLink = $"{_appSettings.FrontendUrl}/reset-password?token={resetToken}&email={Uri.EscapeDataString(user.Email)}";

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

        /// <summary>
        /// Consumes a reset token and sets the new password. The token is single-use, and every
        /// refresh token the user holds is revoked after the commit - a credential change ends
        /// all existing sessions.
        /// </summary>
        public async Task<Result<Guid>> ResetPasswordAsync(ResetPasswordDto resetPasswordDto)
        {
            _logger.LogInformation("Attempting password reset");

            var tokenHash = TokenHasher.Sha256(resetPasswordDto.Token);

            var resetToken = await _context.PasswordResetTokens
                .FirstOrDefaultAsync(rt => rt.TokenHash == tokenHash
                                        && !rt.IsUsed
                                        && rt.ExpiresAt > DateTime.UtcNow);

            if (resetToken == null)
                return Error.BadRequest("Invalid or expired reset token");

            var user = await _context.Users.FindAsync(resetToken.UserId);

            if (user == null)
                return Error.BadRequest("Invalid or expired reset token");

            user.PasswordHash = _passwordHasher.HashPassword(resetPasswordDto.NewPassword);
            

            resetToken.IsUsed = true;
            resetToken.UsedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _redisService.RevokeAllUserTokensAsync(user.Id);

            _logger.LogInformation("Password reset successfully for user: {UserId}", user.Id);

            return user.Id;
        }

        /// <summary>
        /// Validates a Google ID token against our client id and signs the user in, creating the
        /// account on first sight. SSO accounts get a random unusable password and a generated
        /// username that follows the same rules the DTOs enforce, because this path writes the
        /// entity directly and nothing else validates it.
        /// </summary>
        public async Task<Result<AuthResponseDto>> GoogleLoginAsync(GoogleLoginDto dto, string ipAddress)
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

            if (payload.EmailVerified != true)
                return Error.Forbidden("Google account email is not verified");

            var email = NormalizeEmail(payload.Email);

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                user = new User
                {
                    UserName = await GenerateUniqueUsernameAsync(email),
                    Email = email,
                    PasswordHash = _passwordHasher.HashPassword(Guid.NewGuid().ToString()),
                    FirstName = payload.GivenName,
                    LastName = payload.FamilyName,
                    ProfilePicturePath = payload.Picture,
                    IsEmailConfirmed = true,
    
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation("New user created via Google SSO: {Email}", user.Email);

                try
                {
                    await _emailService.SendWelcomeEmailAsync(user.Email, user.FirstName ?? user.UserName);
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
                
                await _context.SaveChangesAsync();

                _logger.LogInformation("Existing user logged in via Google: {Email}", user.Email);
            }

            return await GenerateAuthResponseAsync(user, ipAddress);
        }

        /// <summary>
        /// Issues an access/refresh pair and stores the refresh token in Redis before handing
        /// both back to the caller.
        /// </summary>
        private async Task<AuthResponseDto> GenerateAuthResponseAsync(User user, string ipAddress)
        {
            var accessToken = _tokenGenerator.GenerateAccessToken(user);
            var refreshToken = _tokenGenerator.GenerateRefreshToken();
            await StoreRefreshTokenAsync(user.Id, refreshToken, ipAddress);

            return new AuthResponseDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                UserId = user.Id,
                UserName = user.UserName,
                Email = user.Email
            };
        }

        /// <summary>
        /// Changes the password after verifying the current one, revokes every existing session
        /// and signs the caller straight back in with a fresh pair. Revocation runs before the
        /// new pair is issued - the other order would delete the token just created.
        /// </summary>
        public async Task<Result<AuthResponseDto>> ChangePasswordAsync(
            Guid userId, ChangePasswordDto dto, string ipAddress)
        {
            if (dto.NewPassword != dto.ConfirmNewPassword)
                return Error.Validation("Passwords do not match");

            if (dto.NewPassword.Length < 8)
                return Error.Validation("Password must be at least 8 characters");

            var user = await _context.Users.FindAsync(userId);
            if (user is null)
                return Error.NotFound("User not found");

            if (!_passwordHasher.VerifyPassword(dto.CurrentPassword, user.PasswordHash))
                return Error.Validation("Current password is incorrect");

            user.PasswordHash = _passwordHasher.HashPassword(dto.NewPassword);

            await _context.SaveChangesAsync();

            await _redisService.RevokeAllUserTokensAsync(userId);

            _logger.LogInformation("User {UserId} changed their password", userId);

            return await GenerateAuthResponseAsync(user, ipAddress);
        }


        /// <summary>
        /// Writes the refresh token model to Redis keyed by the token's SHA-256 hash, with a TTL
        /// matching its expiry. The plaintext token never reaches storage.
        /// </summary>
        private async Task StoreRefreshTokenAsync(Guid userId, string refreshToken, string ipAddress)
        {
            var tokenModel = new RefreshTokenModel
            {
                UserId = userId,
                ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryInDays),
                CreatedByIp = ipAddress,
                IsRevoked = false
            };

            var expiration = tokenModel.ExpiresAt - DateTime.UtcNow;
            await _redisService.SetRefreshTokenAsync(TokenHasher.Sha256(refreshToken), tokenModel, expiration);
        }

        /// <summary>Cryptographically random six-digit code.</summary>
        private static string GenerateVerificationCode()
        {
            return RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        }

        /// <summary>
        /// Retries the email-derived username with fresh random suffixes until one is free.
        /// </summary>
        private async Task<string> GenerateUniqueUsernameAsync(string email)
        {
            string userName;
            do
            {
                userName = GenerateUsernameFromEmail(email);
            }
            while (await _context.Users.AnyAsync(u => u.UserName == userName));

            return userName;
        }

        /// <summary>
        /// Derives a valid username from the email's local part: letters, digits and underscore
        /// only, capped so the random suffix stays inside the length limit.
        /// </summary>
        private static string GenerateUsernameFromEmail(string email)
        {
            var localPart = email.Split('@')[0];

            // Match the rules RegisterDto and UpdateUserProfileDto enforce: letters, digits
            // and underscore only. SSO writes the entity directly so nothing else validates it.
            var cleaned = new string(localPart
                .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_')
                .ToArray());

            // The "_NNNN" suffix is always 5 chars so cap the base at 19 to stay inside the
            // 24 char limit. Without this a long address overflows the 50 char column.
            if (cleaned.Length > 19)
                cleaned = cleaned[..19];

            if (cleaned.Length == 0)
                cleaned = "user";

            var suffix = RandomNumberGenerator.GetInt32(1000, 9999);
            return $"{cleaned}_{suffix}";
        }

        /// <summary>
        /// Mail providers treat the mailbox case insensitively, so the address is stored and
        /// compared in one canonical form. RedisService already lowercases its verification keys.
        /// </summary>
        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
    }
}