using Blazored.LocalStorage;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Plantitask.Web.Services;

public class AuthTokenHandler : DelegatingHandler
{
    private readonly ILocalStorageService _localStorage;
    private readonly CustomAuthStateProvider _authStateProvider;
    private readonly SemaphoreSlim  _refreshLock = new(1,1);

    public AuthTokenHandler(
        ILocalStorageService localStorage,
        CustomAuthStateProvider authStateProvider)
    {
        _localStorage = localStorage;
        _authStateProvider = authStateProvider;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!IsPublicEndpoint(request.RequestUri))
        {
            var token = await _localStorage.GetItemAsStringAsync("authToken");
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var refreshed = await TryRefreshTokenAsync(cancellationToken);

            if (refreshed)
            {
                var newToken = await _localStorage.GetItemAsStringAsync("authToken");
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", newToken);

                var retryRequest = await CloneRequestAsync(request);
                response = await base.SendAsync(retryRequest, cancellationToken);
            }
            else
            {
                await _localStorage.RemoveItemAsync("authToken");
                await _localStorage.RemoveItemAsync("refreshToken");
                _authStateProvider.NotifyUserLogout();
            }
        }

        return response;
    }

    private static bool IsPublicEndpoint(Uri? uri)
    {
        if (uri == null) return true;

        var path = uri.AbsolutePath.ToLowerInvariant();
        return path.Contains("/auth/login")
            || path.Contains("/auth/register")
            || path.Contains("/auth/check-email")
            || path.Contains("/auth/send-verification")
            || path.Contains("/auth/verify-email")
            || path.Contains("/auth/forgot-password")
            || path.Contains("/auth/reset-password")
            || path.Contains("/auth/google-login")
            || path.Contains("/auth/refresh");
    }

    private async Task<bool> TryRefreshTokenAsync(CancellationToken cancellationToken)
    {
        var tokenBeforeLock = await _localStorage.GetItemAsStringAsync("authToken");
        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            var currentToken = await _localStorage.GetItemAsStringAsync("authToken");
            if (tokenBeforeLock != currentToken)
                return true;

            var refreshToken = await _localStorage.GetItemAsStringAsync("refreshToken");
            if (string.IsNullOrEmpty(refreshToken))
                return false;

            var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh")
            {
                Content = JsonContent.Create(new { RefreshToken = refreshToken })
            };

            var response = await base.SendAsync(refreshRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return false;

            var authResponse = await response.Content
                .ReadFromJsonAsync<Models.AuthResponse>(cancellationToken: cancellationToken);

            if (authResponse == null)
                return false;

            await _localStorage.SetItemAsStringAsync("authToken", authResponse.AccessToken);
            await _localStorage.SetItemAsStringAsync("refreshToken", authResponse.RefreshToken);
            _authStateProvider.NotifyUserAuthentication(authResponse.AccessToken);

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content != null)
        {
            var content = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(content);

            if (request.Content.Headers.ContentType != null)
                clone.Content.Headers.ContentType = request.Content.Headers.ContentType;
        }

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }
}