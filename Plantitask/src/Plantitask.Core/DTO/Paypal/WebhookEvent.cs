
namespace Plantitask.Core.DTO.Paypal
{
    public class PayPalWebhookEvent
    {
        public string Id { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public PayPalWebhookResource Resource { get; set; } = new();
    }
    public class PayPalWebhookResource
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? CustomId { get; set; }
        public string? BillingAgreementId { get; set; }
        public PayPalBillingInfo? BillingInfo { get; set; }
    }

    public class PayPalBillingInfo
    {
        public string? NextBillingTime { get; set; }
    }
}
