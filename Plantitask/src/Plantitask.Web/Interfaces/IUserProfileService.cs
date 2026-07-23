
using Plantitask.Web.Models;

using Plantitask.Core.DTO.Users;
using Plantitask.Core.DTO.Paypal;
namespace Plantitask.Web.Interfaces;

    public interface IUserProfileService
    {
        Task<ServiceResult<UserProfileDto>> GetProfileAsync();
        Task<ServiceResult<UserProfileDto>> UpdateProfileAsync(UpdateUserProfileDto dto);
        Task<ServiceResult<ProfilePictureResponse>> UploadProfilePictureAsync(Stream fileStream, string fileName, string contentType);
        Task<ServiceResult<MessageResponse>> RemoveProfilePictureAsync();
        Task<ServiceResult<MessageResponse>> ChangePasswordAsync(ChangePasswordDto dto);
    }
