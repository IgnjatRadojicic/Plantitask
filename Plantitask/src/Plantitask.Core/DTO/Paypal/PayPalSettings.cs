

namespace Plantitask.Core.DTO.Paypal
{
    public class PayPalSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://api-m.paypal.com";
        public string RecurringPlanId { get; set; } = string.Empty; 
        public decimal OneTimePrice { get; set; } = 4.99m;
        public string Currency { get; set; } = "USD";
        public string WebhookId { get; set; } = string.Empty;
    }

}
