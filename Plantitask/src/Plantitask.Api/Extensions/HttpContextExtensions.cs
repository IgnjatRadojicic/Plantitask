namespace Plantitask.Api.Extensions
{
    public static class HttpContextExtensions
    {
        public static string GetClientIpAddress(this HttpContext context)
        {


            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            if(!string.IsNullOrEmpty(forwardedFor))
            {
                return forwardedFor.Split(',')[0].Trim();
            }

            // For possible future nginx use fall back to real-ip

            var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();

            if(!string.IsNullOrEmpty(realIp))
            {
                return realIp;
            }

            return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
        }
        public static string GetUserAgent(this HttpContext context)
        {
            return context.Request.Headers["User-Agent"].FirstOrDefault() ?? "Unknown";
        }
    }
}
