using Plantitask.Web.Models;

using Plantitask.Core.DTO.Auth;
namespace Plantitask.Web.Interfaces
{
    public interface IAuthService
    {

        Task<ServiceResult<CheckEmailResponseDto>> CheckEmailAsync(string email);
        Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginDto request);
        Task<ServiceResult<MessageResponse>> SendVerificationCodeAsync(string email);
        Task<ServiceResult<MessageResponse>> VerifyCodeAsync(string email, string code);
        Task<ServiceResult<AuthResponseDto>> RegisterAsync(RegisterDto request);
        Task<ServiceResult<AuthResponseDto>> GoogleLoginAsync(string idToken);
        Task<ServiceResult<MessageResponse>> ForgotPasswordAsync(string email);
        Task<ServiceResult<MessageResponse>> ResetPasswordAsync(ResetPasswordDto request);
        Task<ServiceResult<AuthResponseDto>> RefreshTokenAsync(string refreshToken);
        Task LogoutAsync();
        Task<string?> GetTokenAsync();

    }
}
