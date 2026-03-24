using Plantitask.Core.Common;
using Plantitask.Core.DTO.Paypal;

namespace Plantitask.Core.Interfaces
{
    public interface IPayPalService
    {
        Task<Result<CreateSubscriptionResponse>> CreateSubscriptionAsync(Guid userId, CreateSubscriptionRequest request);
        Task<Result> ActivateSubscriptionAsync(Guid userId, string subscriptionId);
        Task<Result> CancelSubscriptionAsync(Guid userId);

        Task<Result<CreateOrderResponse>> CreateOneTimeOrderAsync(Guid userId, CreateOrderRequest request);
        Task<Result<CaptureOrderResponse>> CaptureOrderAsync(Guid userId, string orderId);

        Task<Result<PremiumStatusDto>> GetPremiumStatusAsync(Guid userId);

        Task HandleWebhookAsync(string body, Dictionary<string, string> headers);
    }
}