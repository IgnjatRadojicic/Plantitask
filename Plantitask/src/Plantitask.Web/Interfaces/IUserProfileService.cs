
using Plantitask.Web.Models;

using Plantitask.Contracts.DTO.Users;
using Plantitask.Contracts.DTO.Paypal;
using Plantitask.Contracts.DTO.Auth;
using Plantitask.Contracts.DTO.Plans;
namespace Plantitask.Web.Interfaces;

    public interface IUserProfileService
    {
        Task<ServiceResult<UserProfileDto>> GetProfileAsync();
        Task<ServiceResult<EntitlementsDto>> GetEntitlementsAsync();
        Task<ServiceResult<UserProfileDto>> UpdateProfileAsync(UpdateUserProfileDto dto);
        Task<ServiceResult<ProfilePictureResponse>> UploadProfilePictureAsync(Stream fileStream, string fileName, string contentType);
        Task<ServiceResult<MessageResponse>> RemoveProfilePictureAsync();
        Task<ServiceResult<AuthResponseDto>> ChangePasswordAsync(ChangePasswordDto dto);
    }
