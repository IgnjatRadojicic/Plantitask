using Blazored.LocalStorage;
using System.Net;
using System.Net.Http.Json;
using Plantitask.Core.DTO.Auth;
using Plantitask.Web.Interfaces;

namespace Plantitask.Web.Services
{
    public class SessionService : ISessionService
    {
        public const string AuthClientName = "PlantitaskAuth";

        private const string TokenKey = "authToken";
        private const string RefreshTokenKey = "refreshToken";

        private readonly ILocalStorageService _localStorage;
        private readonly IHttpClientFactory _httpFactory;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        public event Action<string?>? OnTokensChanged;

        public SessionService(ILocalStorageService localStorage, IHttpClientFactory httpFactory)
        {
            _localStorage = localStorage;
            _httpFactory = httpFactory;
        }

        public async Task<string?> GetAccessTokenAsync() =>
            await _localStorage.GetItemAsStringAsync(TokenKey);

        public async Task<string?> GetRefreshTokenAsync() =>
               await _localStorage.GetItemAsStringAsync(RefreshTokenKey);

        public async Task SetTokensAsync(AuthResponseDto auth)
        {
            await _localStorage.SetItemAsStringAsync(TokenKey, auth.AccessToken);
            await _localStorage.SetItemAsStringAsync(RefreshTokenKey, auth.RefreshToken);
            OnTokensChanged?.Invoke(auth.AccessToken);
        }

        public async Task ClearAsync()
        {
            await _localStorage.RemoveItemAsync(TokenKey);
            await _localStorage.RemoveItemAsync(RefreshTokenKey);
            OnTokensChanged?.Invoke(null);
        }

        public async Task<string?> TryRefreshAsync()
        {
            var tokenBeforeLock = await GetAccessTokenAsync();
            await _refreshLock.WaitAsync();

            try
            {
                var current = await GetAccessTokenAsync();
                if (current != tokenBeforeLock && !string.IsNullOrWhiteSpace(current))
                    return current;

                var refreshToken = await GetRefreshTokenAsync();
                if (string.IsNullOrWhiteSpace(refreshToken))
                    return null;

                var http = _httpFactory.CreateClient(AuthClientName);

                var response = await http.PostAsJsonAsync("api/auth/refresh",
                    new RefreshTokenDto { RefreshToken = refreshToken });

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode is HttpStatusCode.Unauthorized
                                            or HttpStatusCode.Forbidden)
                        await ClearAsync();

                    return null;
                }

                var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
                if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
                    return null;
                await SetTokensAsync(auth);
                return auth.AccessToken;
            } catch (HttpRequestException)
            {
                return null;
            } 
            finally
            {
                _refreshLock.Release();
            }
        }
    }
}
