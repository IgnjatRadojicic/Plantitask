using System.Net.Http.Json;
using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;
using Plantitask.Core.DTO.Auth;
namespace Plantitask.Web.Services
{
    public class AuthService : BaseApiService, IAuthService
    {
        private readonly ISessionService _session;

        public AuthService(HttpClient http, ISessionService session) : base(http)
        {
            _session = session;
        }
        public async Task<ServiceResult<CheckEmailResponseDto>> CheckEmailAsync(string email)
        {
            return await PostAsync<CheckEmailResponseDto>(
                "api/auth/check-email",
                new CheckEmailDto { Email = email });
        }

        public async Task<ServiceResult<MessageResponse>> ForgotPasswordAsync(string email)
        {
            return await PostAsync<MessageResponse>(
                "api/auth/forgot-password",
                new ForgotPasswordDto { Email = email });
        }


        public async Task<ServiceResult<AuthResponseDto>> GoogleLoginAsync(string idToken)
        {
            var result = await PostAsync<AuthResponseDto>(
                "api/auth/google-login",
                new GoogleLoginDto { IdToken = idToken });

            if (result.Success && result.Data != null)
                await _session.SetTokensAsync(result.Data);

            return result;
        }

        public async Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginDto request)
        {
            var result = await PostAsync<AuthResponseDto>("api/auth/login", request);

            if (result.Success && result.Data != null)
                await _session.SetTokensAsync(result.Data);

            return result;
        }

        public async Task LogoutAsync()
        {
            var refreshToken = await _session.GetRefreshTokenAsync();

            if (!string.IsNullOrEmpty(refreshToken))
            {
                try
                {
                    await Http.PostAsJsonAsync("api/auth/logout",
                        new RefreshTokenDto { RefreshToken = refreshToken });
                }
                catch { }
            }

            await _session.ClearAsync();
        }

        public async Task<ServiceResult<AuthResponseDto>> RegisterAsync(RegisterDto request)
        {
            var result = await PostAsync<AuthResponseDto>("api/auth/register", request);

            if (result.Success && result.Data != null)
                await _session.SetTokensAsync(result.Data);

            return result;
        }

        public async Task<ServiceResult<MessageResponse>> ResetPasswordAsync(ResetPasswordDto request)
        {
            return await PostAsync<MessageResponse>("api/auth/reset-password", request);
        }

        public async Task<ServiceResult<MessageResponse>> SendVerificationCodeAsync(string email)
        {
            return await PostAsync<MessageResponse>(
                "api/auth/send-verification",
                new SendVerificationRequest { Email = email });
        }

        public async Task<ServiceResult<MessageResponse>> VerifyCodeAsync(string email, string code)
        {
            return await PostAsync<MessageResponse>(
                "api/auth/verify-email",
                new VerifyEmailDto { Email = email, Code = code });
        }

        public Task AdoptSessionAsync(AuthResponseDto auth) => _session.SetTokensAsync(auth);
    }
}
