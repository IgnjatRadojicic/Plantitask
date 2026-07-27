using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Users;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Validation;

namespace Plantitask.Infrastructure.Services;

public class UserProfileService : IUserProfileService
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRedisService _redisService;
    private readonly ILogger<UserProfileService> _logger;

    // Matches [RequestSizeLimit] on UserProfileController.UploadProfilePicture.
    private const int MaxProfilePictureMb = 5;

    public UserProfileService(
        IApplicationDbContext context,
        IFileStorageService fileStorage,
        IPasswordHasher passwordHasher,
        IRedisService redisService,
        ILogger<UserProfileService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _passwordHasher = passwordHasher;
        _redisService = redisService;
        _logger = logger;
    }

    public async Task<Result<UserProfileDto>> GetProfileAsync(Guid userId)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user is null)
            return Error.NotFound("User not found");

        return MapToDto(user);
    }

    public async Task<Result<UserProfileDto>> UpdateProfileAsync(Guid userId, UpdateUserProfileDto dto)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user is null)
            return Error.NotFound("User not found");

        if (!string.IsNullOrWhiteSpace(dto.UserName))
        {
            // Trim first so the name we check for availability is the name we store.
            var requested = dto.UserName.Trim();

            // Ordinal not OrdinalIgnoreCase. Usernames are case sensitive here so Bob and bob
            // are two different handles and Bob -> bob is a real rename that must be checked.
            if (!string.Equals(requested, user.UserName, StringComparison.Ordinal))
            {
                var usernameTaken = await _context.Users
                    .AnyAsync(u => u.UserName == requested && u.Id != userId);

                if (usernameTaken)
                    return Error.Conflict("Username is already taken");

                user.UserName = requested;
            }
        }

        if (dto.FirstName is not null)
            user.FirstName = dto.FirstName.Trim();

        if (dto.LastName is not null)
            user.LastName = dto.LastName.Trim();

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The unique index is the real guard. Two requests can both pass the check above.
            return Error.Conflict("Username is already taken");
        }

        _logger.LogInformation("User {UserId} updated their profile", userId);

        return MapToDto(user);
    }

    public async Task<Result<string>> UploadProfilePictureAsync(
        Guid userId, Stream fileStream, string fileName)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user is null)
            return Error.NotFound("User not found");

        var validation = await FileUploadRules.ValidateAsync(
            fileStream, fileName, MaxProfilePictureMb, FileUploadRules.ImageExtensions);
        if (validation.IsFailure)
            return validation.Error!;

        if (!string.IsNullOrEmpty(user.ProfilePictureUrl))
        {
            try { await _fileStorage.DeleteFileAsync(user.ProfilePictureUrl); }
            catch { }
        }

        // The storage layer picks the stored name; the original is metadata only.
        var storagePath = await _fileStorage.UploadFileAsync(
            fileStream, fileName, FileUploadRules.ContentTypeFor(validation.Value!));
        user.ProfilePictureUrl = _fileStorage.GetFileUrl(storagePath);

        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} updated their profile picture", userId);

        return user.ProfilePictureUrl;
    }

    public async Task<Result> RemoveProfilePictureAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user is null)
            return Error.NotFound("User not found");

        if (!string.IsNullOrEmpty(user.ProfilePictureUrl))
        {
            try { await _fileStorage.DeleteFileAsync(user.ProfilePictureUrl); }
            catch { }
        }

        user.ProfilePictureUrl = null;

        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} removed their profile picture", userId);

        return Result.Success();
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordDto dto)
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

        return Result.Success();
    }

    private static UserProfileDto MapToDto(User user)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            ProfilePictureUrl = user.ProfilePictureUrl,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            IsPremium = user.HasActivePremium,
            SubscriptionType = user.SubscriptionType,
            PremiumExpiresAt = user.PremiumExpiresAt,
            PremiumStartedAt = user.PremiumStartedAt,
            MaxGroups = user.MaxGroups
        };
    }
}