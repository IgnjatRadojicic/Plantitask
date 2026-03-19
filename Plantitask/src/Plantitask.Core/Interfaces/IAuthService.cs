using Plantitask.Core.Common;
using Plantitask.Core.DTO.Auth;

namespace Plantitask.Core.Interfaces
{
    public interface IAuthService
    {
        Task<Result<AuthResponseDto>> RegisterAsync(RegisterDto registerDto);
        Task<Result<AuthResponseDto>> LoginAsync(LoginDto loginDto);
        Task<Result<AuthResponseDto>> RefreshTokenAsync(string refreshToken);
        Task<Result> LogoutAsync(string refreshToken);
        Task<Result<CheckEmailResponseDto>> CheckEmailAsync(string email);
        Task<Result> SendVerificationCodeAsync(string email);
        Task<Result> VerifyEmailCodeAsync(string email, string code);
        Task<Result> ForgotPasswordAsync(string email);
        Task<Result<Guid>> ResetPasswordAsync(ResetPasswordDto resetPasswordDto);
        Task<Result<AuthResponseDto>> GoogleLoginAsync(GoogleLoginDto dto);
    }
}