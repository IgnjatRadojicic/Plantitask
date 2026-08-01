using Plantitask.Web.Interfaces;
using System.Net;
using System.Net.Http.Headers;
namespace Plantitask.Web.Services;

public class AuthTokenHandler : DelegatingHandler
{
    private readonly ISessionService _session;

    public AuthTokenHandler(ISessionService session)
    {
        _session = session;
    }
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!IsPublicEndpoint(request.RequestUri))
        {
            var token = await _session.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token);
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        var newToken = await _session.TryRefreshAsync();
        if (newToken is null)
            return response;   // SessionService already cleared + notified if it was terminal

        var retry = await CloneRequestAsync(request);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);

        return await base.SendAsync(retry, cancellationToken);
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