using Blazored.LocalStorage;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Plantitask.Core.DTO.Auth;
namespace Plantitask.Web.Services
{
    public class AuthService : BaseApiService, IAuthService
    {

        private readonly ILocalStorageService _localStorage;
        private readonly CustomAuthStateProvider _authStateProvider;

        private const string TokenKey = "authToken";
        private const string RefreshTokenKey = "refreshToken";

        public AuthService(
            HttpClient http,
            ILocalStorageService localStorage,
            CustomAuthStateProvider authStateProvider) : base(http)
        {
            _localStorage = localStorage;
            _authStateProvider = authStateProvider;
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
                await StoreTokensAndNotify(result.Data);

            return result;
        }

        public async Task<ServiceResult<AuthResponseDto>> LoginAsync(LoginDto request)
        {
            var result = await PostAsync<AuthResponseDto>("api/auth/login", request);

            if (result.Success && result.Data != null)
                await StoreTokensAndNotify(result.Data);

            return result;
        }

        public async Task LogoutAsync()
        {
            var refreshToken = await _localStorage.GetItemAsStringAsync(RefreshTokenKey);

            if (!string.IsNullOrEmpty(refreshToken))
            {
                try
                {
                    await Http.PostAsJsonAsync("api/auth/logout",
                        new RefreshTokenDto { RefreshToken = refreshToken });
                }
                catch { }
            }

            await _localStorage.RemoveItemAsync(TokenKey);
            await _localStorage.RemoveItemAsync(RefreshTokenKey);
            _authStateProvider.NotifyUserLogout();
        }

        public async Task<ServiceResult<AuthResponseDto>> RefreshTokenAsync(string refreshToken)
        {
            var result = await PostAsync<AuthResponseDto>(
                "api/auth/refresh",
                new RefreshTokenDto { RefreshToken = refreshToken });

            if (result.Success && result.Data != null)
                await StoreTokensAndNotify(result.Data);

            return result;
        }

        public async Task<ServiceResult<AuthResponseDto>> RegisterAsync(RegisterDto request)
        {
            var result = await PostAsync<AuthResponseDto>("api/auth/register", request);

            if (result.Success && result.Data != null)
                await StoreTokensAndNotify(result.Data);

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

        public async Task<string?> GetTokenAsync()
        {
            return await _localStorage.GetItemAsStringAsync(TokenKey);
        }

        private async Task StoreTokensAndNotify(AuthResponseDto auth)
        {
            await _localStorage.SetItemAsStringAsync(TokenKey, auth.AccessToken);
            await _localStorage.SetItemAsStringAsync(RefreshTokenKey, auth.RefreshToken);
            _authStateProvider.NotifyUserAuthentication(auth.AccessToken);
        }
    }
}
