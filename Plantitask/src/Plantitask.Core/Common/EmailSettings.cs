
namespace Plantitask.Core.Common
{
    public class EmailSettings
    {
        public string Provider { get; set; } = "Smtp";
        public string SendGridApiKey { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
    }
}
