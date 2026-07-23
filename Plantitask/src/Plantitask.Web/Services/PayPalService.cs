using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

using Plantitask.Core.DTO.Paypal;
namespace Plantitask.Web.Services;

public class PayPalService : BaseApiService, IPayPalService
{
    public PayPalService(HttpClient http) : base(http) { }

    public Task<ServiceResult<PremiumStatusDto>> GetStatusAsync()
        => GetAsync<PremiumStatusDto>("api/premium/status");

    public Task<ServiceResult<CreateSubscriptionResponse>> CreateSubscriptionAsync(
        string returnUrl, string cancelUrl)
        => PostAsync<CreateSubscriptionResponse>("api/premium/subscribe", new
        {
            ReturnUrl = returnUrl,
            CancelUrl = cancelUrl
        });

    public Task<ServiceResult<object>> ActivateSubscriptionAsync(string subscriptionId)
        => PostAsync<object>($"api/premium/subscribe/activate?subscriptionId={subscriptionId}", new { });

    public Task<ServiceResult<CreateOrderResponse>> CreateOneTimeOrderAsync(
        string returnUrl, string cancelUrl)
        => PostAsync<CreateOrderResponse>("api/premium/onetime", new
        {
            ReturnUrl = returnUrl,
            CancelUrl = cancelUrl
        });

    public Task<ServiceResult<CaptureOrderResponse>> CaptureOrderAsync(string orderId)
        => PostAsync<CaptureOrderResponse>($"api/premium/onetime/capture?orderId={orderId}", new { });

    public Task<ServiceResult<object>> CancelAsync()
        => PostAsync<object>("api/premium/cancel", new { });
}