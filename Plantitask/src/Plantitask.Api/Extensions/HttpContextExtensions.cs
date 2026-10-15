namespace Plantitask.Api.Extensions
{
    public static class HttpContextExtensions
    {
        public static string GetClientIpAddress(this HttpContext context)
        {
            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        // Client controlled and unbounded, so it is cut to the column size here where it enters.
        public static string? GetUserAgent(this HttpContext context)
        {
            var userAgent = context.Request.Headers["User-Agent"].FirstOrDefault();
            return userAgent is { Length: > 500 } ? userAgent[..500] : userAgent;
        }
    }
}
