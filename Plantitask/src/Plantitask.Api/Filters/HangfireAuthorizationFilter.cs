using Hangfire.Dashboard;
using System.Net;

namespace Plantitask.Api.Filters;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    // Will move implementation in the future to Azure Credentials
    private static readonly string[] AllowedAdmins = { "ignjatradojicic@gmail.com" };

    private readonly bool _isDevelopment;

    public HangfireAuthorizationFilter(IWebHostEnvironment environment)
    {
        _isDevelopment = environment.IsDevelopment();
    }

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // The API only registers JWT bearer, and a browser navigating to /hangfire sends no
        // Authorization header, so the claims check below can never pass from a browser.
        // In development the dashboard is opened by hand, so allow it from this machine only.
        if (_isDevelopment && IsLocalRequest(httpContext))
            return true;

        if (!httpContext.User.Identity?.IsAuthenticated ?? true)
            return false;

        var email = httpContext.User.FindFirst("email")?.Value
                    ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

        return AllowedAdmins.Contains(email, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsLocalRequest(HttpContext httpContext)
    {
        var remote = httpContext.Connection.RemoteIpAddress;

        if (remote == null)
            return false;

        if (IPAddress.IsLoopback(remote))
            return true;

        var local = httpContext.Connection.LocalIpAddress;

        return local != null && remote.Equals(local);
    }
}
