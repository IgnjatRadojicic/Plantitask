
namespace Plantitask.Core.DTO.Paypal
{
    public class CreateSubscriptionRequest
    {
        public string ReturnUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
    }

    public class CreateSubscriptionResponse
    {
        public string SubscriptionId { get; set; } = string.Empty;
        public string ApprovalUrl { get; set; } = string.Empty;
    }

}
