

using Plantitask.Core.Common;
using Plantitask.Core.DTO.Users;
using System.Runtime.CompilerServices;

namespace Plantitask.Core.Interfaces
{
    public interface IUserProfileService
    {
        Task<Result<UserProfileDto>> GetProfileAsync(Guid userId);
        Task<Result<UserProfileDto>> UpdateProfileAsync(Guid userId, UpdateUserProfileDto dto);
        Task<Result<string>> UploadProfilePictureAsync(Guid userId, Stream fileStream, string fileName);
        Task<Result> RemoveProfilePictureAsync(Guid userId);
        Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordDto dto);
    }
}
