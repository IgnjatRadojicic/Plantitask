using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Users;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Projections;
using Plantitask.Core.Validation;

namespace Plantitask.Infrastructure.Services;

/// <summary>
/// The caller's own profile: reading it, editing names and managing the profile picture.
/// Everything here is self-scoped - the userId always comes from the JWT, never from input.
/// </summary>
public class UserProfileService : IUserProfileService
{
    private readonly IApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<UserProfileService> _logger;

    // Matches [RequestSizeLimit] on UserProfileController.UploadProfilePicture.
    private const int MaxProfilePictureMb = 5;

    /// <summary>
    /// The projection compiled for in-memory use. One definition of the profile shape with two
    /// callers: EF translates the Expression to SQL on a read, and this invokes it against an
    /// already-tracked entity after a write, so a mutation response costs no second round trip.
    /// Static because compiling is not free and the result is good for the life of the process.
    /// </summary>
    private static readonly Func<User, UserProfileDto> MapProfile =
        UserProjections.ToProfileDto.Compile();

    public UserProfileService(
        IApplicationDbContext context,
        IFileStorageService fileStorage,
        ILogger<UserProfileService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    /// <summary>
    /// The caller's profile. Identity only: premium state lives behind
    /// GET /api/user/profile/entitlements, so this service never needs to know what a plan is.
    /// </summary>
    public async Task<Result<UserProfileDto>> GetProfileAsync(Guid userId)
    {
        var profile = await _context.Users
            .Where(u => u.Id == userId)
            .Select(UserProjections.ToProfileDto)
            .FirstOrDefaultAsync();

        if (profile is null)
            return Error.NotFound("User not found");

        return profile;
    }

    /// <summary>
    /// Updates username and names. Usernames are case sensitive by decision, so the rename
    /// check is Ordinal, and the unique index is the real guard - the availability check just
    /// gives a nicer error, and the DbUpdateException catch handles the race it cannot close.
    /// </summary>
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

        return MapProfile(user);
    }

    /// <summary>
    /// Validates and stores a new profile picture, then cleans up the old one after the commit.
    /// The DB write comes first so a failed cleanup can never leave the profile pointing at a
    /// deleted file.
    /// </summary>
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

        // Captured BEFORE the field is overwritten on the next statement.
        var oldPath = user.ProfilePicturePath;

        // The storage layer picks the stored name; the original is metadata only.
        user.ProfilePicturePath = await _fileStorage.UploadFileAsync(
            fileStream, fileName, FileUploadRules.ContentTypeFor(validation.Value!), "avatars");

        await _context.SaveChangesAsync();

        await TryDeleteStoredPictureAsync(oldPath, userId);

        _logger.LogInformation("User {UserId} updated their profile picture", userId);

        return user.ProfilePicturePath!;
    }

    /// <summary>
    /// Clears the profile picture, committing the null first and deleting the stored file
    /// best-effort afterwards - same ordering as the upload path.
    /// </summary>
    public async Task<Result> RemoveProfilePictureAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user is null)
            return Error.NotFound("User not found");

        var oldPath = user.ProfilePicturePath;

        user.ProfilePicturePath = null;

        await _context.SaveChangesAsync();

        await TryDeleteStoredPictureAsync(oldPath, userId);

        _logger.LogInformation("User {UserId} removed their profile picture", userId);

        return Result.Success();
    }

    /// <summary>
    /// Best-effort cleanup of a replaced picture. The DB is the source of truth - a failed
    /// delete must not fail the request, but it must not be invisible either.
    /// </summary>
    private async Task TryDeleteStoredPictureAsync(string? storedValue, Guid userId)
    {
        if (string.IsNullOrEmpty(storedValue))
            return;

        // Google SSO avatars live on Google's CDN so the column holds their absolute URL
        // rather than one of our keys. Not ours to delete.
        if (storedValue.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            await _fileStorage.DeleteFileAsync(storedValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete old profile picture for {UserId}", userId);
        }
    }

}