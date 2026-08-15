using Blazored.LocalStorage;
using System.Net;
using System.Net.Http.Json;
using Plantitask.Core.DTO.Auth;
using Plantitask.Web.Interfaces;
using Microsoft.JSInterop;

namespace Plantitask.Web.Services
{
    public class SessionService : ISessionService
    {
        public const string AuthClientName = "PlantitaskAuth";

        private const string TokenKey = "authToken";
        private const string RefreshTokenKey = "refreshToken";
        private const string RefreshLockName = "plantitask-refresh";
        private readonly ILocalStorageService _localStorage;
        private readonly IHttpClientFactory _httpFactory;
        private readonly IJSRuntime _js;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        public event Action<string?>? OnTokensChanged;

        public SessionService(ILocalStorageService localStorage, 
            IHttpClientFactory httpFactory,
            IJSRuntime js)
        {
            _js = js;
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
                await AcquireBrowserLockAsync();
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

                    // 409 means this token was rotated seconds ago by another tab that got past
                    // the lock - a lost race, not a dead session. The winner writes the new pair
                    // to the localStorage we share, so re-read it rather than tearing the session
                    // down. If the winner has not landed yet, the next attempt reads its refresh
                    // token at the top of this method and recovers on its own.
                    if (response.StatusCode == HttpStatusCode.Conflict)
                    {
                        var afterRace = await GetAccessTokenAsync();

                        return afterRace != tokenBeforeLock && !string.IsNullOrWhiteSpace(afterRace)
                            ? afterRace
                            : null;
                    }

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
                } finally
                {
                    await ReleaseBrowserLockAsync();
                }
            } catch (HttpRequestException)
            {
                return null;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        // Serialises refreshes across every tab in this browser profile, which is exactly the
        // scope that shares localStorage. A missing or unsupported lock is not fatal: the
        // semaphore still covers this tab and the server's grace window covers the rest.
        private async Task AcquireBrowserLockAsync()
        {
            try
            {
                await _js.InvokeVoidAsync("plantitaskLocks.acquire", RefreshLockName);
            }
            catch (JSException)
            {
                // session-lock.js missing or blocked. Degrade instead of taking down refresh.
            }
        }

        // Safe to call even if the acquire failed: release() is a no-op in JS when this tab
        // holds nothing. A leaked browser lock would block every tab on the origin, so this
        // must never be skipped and must never throw.
        private async Task ReleaseBrowserLockAsync()
        {
            try
            {
                await _js.InvokeVoidAsync("plantitaskLocks.release", RefreshLockName);
            }
            catch (JSException)
            {
            }
        }
    }
}
