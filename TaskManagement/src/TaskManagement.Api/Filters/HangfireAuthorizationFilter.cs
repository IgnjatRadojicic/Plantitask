using Hangfire.Dashboard;

namespace TaskManagement.Api.Filters;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        // For development: allow everyone
        // TODO: In production check if user is authenticated and is admin
        return true;
    }
}