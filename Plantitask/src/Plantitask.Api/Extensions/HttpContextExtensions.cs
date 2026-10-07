namespace Plantitask.Api.Extensions
{
    public static class HttpContextExtensions
    {
        public static string GetClientIpAddress(this HttpContext context)
        {
            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }

        public static string GetUserAgent(this HttpContext context)
        {
            return context.Request.Headers["User-Agent"].FirstOrDefault() ?? "Unknown";
        }
    }
}
