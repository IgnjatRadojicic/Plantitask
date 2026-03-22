
namespace Plantitask.Core.DTO.Paypal
{
    public class CreateOrderRequest
    {
        public string ReturnUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
    }

    public class CreateOrderResponse
    {
        public string OrderId { get; set; } = string.Empty;
        public string ApprovaUrl { get; set; } = string.Empty;
    }

    public class CaptureOrderResponse
    {
        public bool Success { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
