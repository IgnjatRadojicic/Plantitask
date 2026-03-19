using Plantitask.Web.Models;

namespace Plantitask.Web.Interfaces
{
    public interface IAuthService
    {

        Task<ServiceResult<CheckEmailResponse>> CheckEmailAsync(string email);
        Task<ServiceResult<AuthResponse>> LoginAsync(LoginRequest request);
        Task<ServiceResult<MessageResponse>> SendVerificationCodeAsync(string email);
        Task<ServiceResult<MessageResponse>> VerifyCodeAsync(string email, string code);
        Task<ServiceResult<AuthResponse>> RegisterAsync(RegisterRequest request);
        Task<ServiceResult<AuthResponse>> GoogleLoginAsync(string idToken);
        Task<ServiceResult<MessageResponse>> ForgotPasswordAsync(string email);
        Task<ServiceResult<MessageResponse>> ResetPasswordAsync(ResetPasswordRequest request);
        Task<ServiceResult<AuthResponse>> RefreshTokenAsync(string refreshToken);
        Task LogoutAsync();
        Task<string?> GetTokenAsync();

    }
}
