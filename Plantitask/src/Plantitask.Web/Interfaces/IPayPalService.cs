using Plantitask.Web.Models;

using Plantitask.Core.DTO.Paypal;
namespace Plantitask.Web.Interfaces
{
    public interface IPayPalService
    {
        Task<ServiceResult<PremiumStatusDto>> GetStatusAsync();
        Task<ServiceResult<CreateSubscriptionResponse>> CreateSubscriptionAsync(string returnUrl, string cancelUrl);
        Task<ServiceResult<object>> ActivateSubscriptionAsync(string subscriptionId);
        Task<ServiceResult<CreateOrderResponse>> CreateOneTimeOrderAsync(string returnUrl, string cancelUrl);
        Task<ServiceResult<CaptureOrderResponse>> CaptureOrderAsync(string orderId);
        Task<ServiceResult<object>> CancelAsync();
    }
}
