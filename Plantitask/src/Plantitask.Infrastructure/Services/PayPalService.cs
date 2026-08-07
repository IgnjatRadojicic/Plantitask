using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Paypal;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    public class PayPalService : IPayPalService
    {
        private const string AccessTokenCacheKey = "paypal-access-token";

        private readonly IApplicationDbContext _db;
        private readonly HttpClient _http;
        private readonly PayPalSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly ILogger<PayPalService> _logger;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public PayPalService(
            IApplicationDbContext db,
            HttpClient http,
            IOptions<PayPalSettings> settings,
            IMemoryCache cache,
            ILogger<PayPalService> logger)
        {
            _db = db;
            _http = http;
            _settings = settings.Value;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// PayPal access tokens live about nine hours but every call was fetching a fresh one,
        /// so a single checkout paid for several extra round trips to PayPal.
        ///
        /// The cache has to be IMemoryCache and not a field. AddHttpClient registers this
        /// service as transient so a field cache would be born and die inside one request and
        /// never serve a second call.
        /// </summary>
        private async Task<string> GetAccessTokenAsync()
        {
            if (_cache.TryGetValue<string>(AccessTokenCacheKey, out var cached) && cached is not null)
                return cached;

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_settings.BaseUrl}/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var token = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();

            // Expire our copy five minutes early so a token is never used in the window where
            // PayPal considers it dead but we do not.
            _cache.Set(AccessTokenCacheKey, token, TimeSpan.FromSeconds(Math.Max(expiresIn - 300, 60)));

            return token;
        }

        public async Task<Result<CreateSubscriptionResponse>> CreateSubscriptionAsync(
            Guid userId, CreateSubscriptionRequest request)
        {
            var token = await GetAccessTokenAsync();

            var body = new
            {
                plan_id = _settings.RecurringPlanId,
                custom_id = userId.ToString(),
                application_context = new
                {
                    brand_name = "Plantitask",
                    return_url = request.ReturnUrl,
                    cancel_url = request.CancelUrl,
                    user_action = "SUBSCRIBE_NOW",
                    shipping_preference = "NO_SHIPPING"
                }
            };

            var httpReq = new HttpRequestMessage(HttpMethod.Post,
                $"{_settings.BaseUrl}/v1/billing/subscriptions");
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpReq.Content = new StringContent(
                JsonSerializer.Serialize(body, _jsonOpts), Encoding.UTF8, "application/json");

            var httpResp = await _http.SendAsync(httpReq);
            var respJson = await httpResp.Content.ReadAsStringAsync();

            if (!httpResp.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal create subscription failed: {Response}", respJson);
                return Error.BadRequest("Failed to create PayPal subscription");
            }

            using var doc = JsonDocument.Parse(respJson);
            var root = doc.RootElement;

            var subscriptionId = root.GetProperty("id").GetString()!;
            var approvalUrl = root.GetProperty("links")
                .EnumerateArray()
                .First(l => l.GetProperty("rel").GetString() == "approve")
                .GetProperty("href").GetString()!;

            return new CreateSubscriptionResponse
            {
                SubscriptionId = subscriptionId,
                ApprovalUrl = approvalUrl
            };
        }

        public async Task<Result> ActivateSubscriptionAsync(Guid userId, string subscriptionId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return Error.NotFound("User not found");

            if (user.IsPremium && user.PayPalSubscriptionId == subscriptionId)
                return Result.Success();

            var token = await GetAccessTokenAsync();
            var httpReq = new HttpRequestMessage(HttpMethod.Get,
                $"{_settings.BaseUrl}/v1/billing/subscriptions/{subscriptionId}");
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var httpResp = await _http.SendAsync(httpReq);
            if (!httpResp.IsSuccessStatusCode)
                return Error.BadRequest("Failed to verify subscription with PayPal");

            var json = await httpResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var status = doc.RootElement.GetProperty("status").GetString();

            if (status != "ACTIVE")
                return Error.BadRequest("Subscription is not active on PayPal");

            GrantPremium(user, "recurring", expiresAt: null);
            user.PayPalSubscriptionId = subscriptionId;

            await _db.SaveChangesAsync();

            _logger.LogInformation("User {UserId} activated recurring premium via subscription {SubId}",
                userId, subscriptionId);

            return Result.Success();
        }

        public async Task<Result> CancelSubscriptionAsync(Guid userId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return Error.NotFound("User not found");

            if (!user.IsPremium)
                return Error.BadRequest("User does not have an active premium subscription");

            if (user.SubscriptionType == "recurring" && !string.IsNullOrEmpty(user.PayPalSubscriptionId))
            {
                var token = await GetAccessTokenAsync();
                var httpReq = new HttpRequestMessage(HttpMethod.Post,
                    $"{_settings.BaseUrl}/v1/billing/subscriptions/{user.PayPalSubscriptionId}/cancel");
                httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                httpReq.Content = new StringContent(
                    JsonSerializer.Serialize(new { reason = "User requested cancellation" }, _jsonOpts),
                    Encoding.UTF8, "application/json");

                var httpResp = await _http.SendAsync(httpReq);
                if (!httpResp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("PayPal cancel subscription failed for user {UserId}", userId);
                }
            }

            RevokePremium(user);
            await _db.SaveChangesAsync();

            _logger.LogInformation("User {UserId} cancelled premium", userId);
            return Result.Success();
        }

        public async Task<Result<CreateOrderResponse>> CreateOneTimeOrderAsync(
            Guid userId, CreateOrderRequest request)
        {
            var token = await GetAccessTokenAsync();

            var body = new
            {
                intent = "CAPTURE",
                purchase_units = new[]
                {
                    new
                    {
                        custom_id = userId.ToString(),
                        description = "Plantitask Premium - 30 Days",
                        amount = new
                        {
                            currency_code = _settings.Currency,
                            value = _settings.OneTimePrice.ToString("F2")
                        }
                    }
                },
                application_context = new
                {
                    brand_name = "Plantitask",
                    return_url = request.ReturnUrl,
                    cancel_url = request.CancelUrl,
                    shipping_preference = "NO_SHIPPING"
                }
            };

            var httpReq = new HttpRequestMessage(HttpMethod.Post,
                $"{_settings.BaseUrl}/v2/checkout/orders");
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpReq.Content = new StringContent(
                JsonSerializer.Serialize(body, _jsonOpts), Encoding.UTF8, "application/json");

            var httpResp = await _http.SendAsync(httpReq);
            var respJson = await httpResp.Content.ReadAsStringAsync();

            if (!httpResp.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal create order failed: {Response}", respJson);
                return Error.BadRequest("Failed to create PayPal order");
            }

            using var doc = JsonDocument.Parse(respJson);
            var root = doc.RootElement;

            var orderId = root.GetProperty("id").GetString()!;
            var approvalUrl = root.GetProperty("links")
                .EnumerateArray()
                .First(l => l.GetProperty("rel").GetString() == "approve")
                .GetProperty("href").GetString()!;

            return new CreateOrderResponse
            {
                OrderId = orderId,
                ApprovalUrl = approvalUrl
            };
        }

        public async Task<Result<CaptureOrderResponse>> CaptureOrderAsync(Guid userId, string orderId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return Error.NotFound("User not found");

            if (user.IsPremium && user.PayPalOrderId == orderId)
            {
                return new CaptureOrderResponse
                {
                    Success = true,
                    OrderId = orderId,
                    Status = "COMPLETED"
                };
            }

            var token = await GetAccessTokenAsync();

            var httpReq = new HttpRequestMessage(HttpMethod.Post,
                $"{_settings.BaseUrl}/v2/checkout/orders/{orderId}/capture");
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            httpReq.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var httpResp = await _http.SendAsync(httpReq);
            var respJson = await httpResp.Content.ReadAsStringAsync();

            if (!httpResp.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal capture order failed: {Response}", respJson);
                return Error.BadRequest("Failed to capture PayPal order");
            }

            using var doc = JsonDocument.Parse(respJson);
            var status = doc.RootElement.GetProperty("status").GetString()!;

            if (status != "COMPLETED")
            {
                return new CaptureOrderResponse
                {
                    Success = false,
                    OrderId = orderId,
                    Status = status
                };
            }

            // The order was stamped with the buyer's id at creation. Without checking it here
            // anyone holding someone else's approved orderId could capture that payment onto
            // their own account.
            var customId = TryReadCustomId(doc.RootElement);

            if (customId is null)
            {
                _logger.LogError(
                    "PayPal capture response for order {OrderId} carried no custom_id, refusing to grant premium",
                    orderId);
                return Error.BadRequest("Could not verify the order belongs to this user");
            }

            if (customId != userId.ToString())
            {
                _logger.LogWarning(
                    "User {UserId} tried to capture order {OrderId} which belongs to {OwnerId}",
                    userId, orderId, customId);
                return Error.Forbidden("Order does not belong to this user");
            }

            GrantPremium(user, "onetime", expiresAt: DateTime.UtcNow.AddDays(OneTimePremiumDays));
            user.PayPalOrderId = orderId;

            await _db.SaveChangesAsync();

            _logger.LogInformation("User {UserId} activated one-time premium via order {OrderId}",
                userId, orderId);

            return new CaptureOrderResponse
            {
                Success = true,
                OrderId = orderId,
                Status = status
            };
        }

        public async Task<Result<PremiumStatusDto>> GetPremiumStatusAsync(Guid userId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return Error.NotFound("User not found");

            if (user.IsPremium
                && user.SubscriptionType == "onetime"
                && user.PremiumExpiresAt.HasValue
                && user.PremiumExpiresAt.Value <= DateTime.UtcNow)
            {
                RevokePremium(user);
                await _db.SaveChangesAsync();

                return new PremiumStatusDto { IsPremium = false, MaxGroups = FreeMaxGroups };
            }

            return new PremiumStatusDto
            {
                IsPremium = user.HasActivePremium,
                SubscriptionType = user.SubscriptionType,
                ExpiresAt = user.PremiumExpiresAt,
                StartedAt = user.PremiumStartedAt,
                CanUseDarkMode = user.HasActivePremium,
                MaxGroups = user.HasActivePremium ? PremiumMaxGroups : FreeMaxGroups
            };
        }

        /// <summary>
        /// The status code this produces is a protocol with PayPal's retry system and not
        /// politeness. 2xx means never send this again, 4xx means do not retry because the
        /// request itself is bad, 5xx means please retry.
        ///
        /// Processing failures are deliberately left to throw. The middleware turns them into
        /// a 500 and PayPal redelivers, which is what we want when a real event could not be
        /// applied. Swallowing them would return 200 and the event would be lost forever.
        /// </summary>
        public async Task<Result> HandleWebhookAsync(string body, Dictionary<string, string> headers)
        {
            if (!await VerifyWebhookSignatureAsync(body, headers))
            {
                _logger.LogWarning("PayPal webhook signature verification failed");
                return Error.Unauthorized("Invalid webhook signature");
            }

            var webhookEvent = JsonSerializer.Deserialize<PayPalWebhookEvent>(body, _jsonOpts);
            if (webhookEvent is null)
                return Error.BadRequest("Malformed webhook event");

            if (string.IsNullOrEmpty(webhookEvent.Id))
                return Error.BadRequest("Webhook event has no id");

            _logger.LogInformation("PayPal webhook: {EventType} for resource {ResourceId}",
                webhookEvent.EventType, webhookEvent.Resource.Id);

            // PayPal redelivers. Keying on the event id means a handler stays safe even if
            // someone later writes one that is not naturally repeatable, which is the failure
            // an absolute-update handler hides until the day it changes.
            if (await _db.ProcessedWebhookEvents.AnyAsync(e => e.EventId == webhookEvent.Id))
            {
                _logger.LogInformation("PayPal webhook {EventId} already processed, ignoring duplicate",
                    webhookEvent.Id);
                return Result.Success();
            }

            _db.ProcessedWebhookEvents.Add(new ProcessedWebhookEvent
            {
                EventId = webhookEvent.Id,
                EventType = webhookEvent.EventType
            });

            switch (webhookEvent.EventType)
            {
                case "BILLING.SUBSCRIPTION.ACTIVATED":
                    await HandleSubscriptionActivated(webhookEvent);
                    break;

                case "PAYMENT.SALE.COMPLETED":
                    await HandlePaymentCompleted(webhookEvent);
                    break;

                case "BILLING.SUBSCRIPTION.CANCELLED":
                case "BILLING.SUBSCRIPTION.SUSPENDED":
                case "BILLING.SUBSCRIPTION.EXPIRED":
                    await HandleSubscriptionCancelled(webhookEvent);
                    break;

                case "BILLING.SUBSCRIPTION.PAYMENT.FAILED":
                    await HandlePaymentFailed(webhookEvent);
                    break;

                case "PAYMENT.CAPTURE.COMPLETED":
                    await HandleOneTimeCaptureCompleted(webhookEvent);
                    break;
            }

            // Handlers stage their changes and this is the only save, so the processed marker
            // and the premium change commit together or not at all. A handler that throws
            // leaves no marker and PayPal redelivers.
            await _db.SaveChangesAsync();

            return Result.Success();
        }

        private async Task HandleSubscriptionActivated(PayPalWebhookEvent evt)
        {
            var customId = evt.Resource.CustomId;
            if (string.IsNullOrEmpty(customId) || !Guid.TryParse(customId, out var userId)) return;

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return;

            GrantPremium(user, "recurring", expiresAt: null);
            user.PayPalSubscriptionId = evt.Resource.Id;
        }

        private async Task HandlePaymentCompleted(PayPalWebhookEvent evt)
        {
            var billingAgreementId = evt.Resource.BillingAgreementId;
            if (string.IsNullOrEmpty(billingAgreementId)) return;

            var user = await _db.Users.FirstOrDefaultAsync(
                u => u.PayPalSubscriptionId == billingAgreementId);
            if (user is null) return;

            GrantPremium(user, "recurring", expiresAt: null);

            _logger.LogInformation("Recurring payment completed for user {UserId}, agreement {AgreementId}",
                user.Id, billingAgreementId);
        }

        /// <summary>
        /// The safety net for one time orders. Without it CaptureOrderAsync from the browser is
        /// the only path to one time premium, so a user who pays and closes the tab before the
        /// redirect gets nothing while PayPal has their money and nothing anywhere notices.
        ///
        /// Recurring already had four events covering it. This is the equivalent for orders.
        /// Whichever of the two paths arrives first wins and the other is a no op, because the
        /// capture path stamps PayPalOrderId and this checks it.
        /// </summary>
        private async Task HandleOneTimeCaptureCompleted(PayPalWebhookEvent evt)
        {
            var customId = evt.Resource.CustomId;
            if (string.IsNullOrEmpty(customId) || !Guid.TryParse(customId, out var userId))
            {
                _logger.LogWarning("Capture completed webhook {EventId} had no usable custom_id", evt.Id);
                return;
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null)
                return;

            // The browser capture already granted this exact order, nothing to do.
            if (user.PayPalOrderId == evt.Resource.Id && user.HasActivePremium)
                return;

            GrantPremium(user, "onetime", expiresAt: DateTime.UtcNow.AddDays(OneTimePremiumDays));
            user.PayPalOrderId = evt.Resource.Id;

            _logger.LogInformation(
                "One time premium granted to user {UserId} from capture webhook for order {OrderId}",
                userId, evt.Resource.Id);
        }

        private async Task HandleSubscriptionCancelled(PayPalWebhookEvent evt)
        {
            var user = await _db.Users.FirstOrDefaultAsync(
                u => u.PayPalSubscriptionId == evt.Resource.Id);
            if (user is null) return;

            RevokePremium(user);
        }

        /// <summary>
        /// No revoke here. PayPal retries a failed subscription payment for several days and
        /// only sends SUSPENDED or CANCELLED or EXPIRED once it gives up, and those already
        /// revoke. Revoking on the first bounce takes premium away from anyone whose card
        /// expired even though the retry two days later usually succeeds.
        /// </summary>
        private async Task HandlePaymentFailed(PayPalWebhookEvent evt)
        {
            var user = await _db.Users.FirstOrDefaultAsync(
                u => u.PayPalSubscriptionId == evt.Resource.Id);
            if (user is null) return;

            _logger.LogWarning("Payment failed for user {UserId} subscription {SubId}, awaiting PayPal dunning outcome",
                user.Id, evt.Resource.Id);
        }

        private async Task<bool> VerifyWebhookSignatureAsync(string body, Dictionary<string, string> headers)
        {
            try
            {
                var token = await GetAccessTokenAsync();

                headers.TryGetValue("PAYPAL-AUTH-ALGO", out var authAlgo);
                headers.TryGetValue("PAYPAL-CERT-URL", out var certUrl);
                headers.TryGetValue("PAYPAL-TRANSMISSION-ID", out var transmissionId);
                headers.TryGetValue("PAYPAL-TRANSMISSION-SIG", out var transmissionSig);
                headers.TryGetValue("PAYPAL-TRANSMISSION-TIME", out var transmissionTime);

                var verifyBody = new
                {
                    auth_algo = authAlgo ?? "",
                    cert_url = certUrl ?? "",
                    transmission_id = transmissionId ?? "",
                    transmission_sig = transmissionSig ?? "",
                    transmission_time = transmissionTime ?? "",
                    webhook_id = _settings.WebhookId,
                    webhook_event = JsonSerializer.Deserialize<JsonElement>(body)
                };

                var httpReq = new HttpRequestMessage(HttpMethod.Post,
                    $"{_settings.BaseUrl}/v1/notifications/verify-webhook-signature");
                httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                httpReq.Content = new StringContent(
                    JsonSerializer.Serialize(verifyBody, _jsonOpts),
                    Encoding.UTF8, "application/json");

                var httpResp = await _http.SendAsync(httpReq);
                var json = await httpResp.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var status = doc.RootElement.GetProperty("verification_status").GetString();
                return status == "SUCCESS";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook verification error");
                return false;
            }
        }

        private const int PremiumMaxGroups = 10;
        private const int FreeMaxGroups = 5;
        private const int OneTimePremiumDays = 30;

        /// <summary>
        /// custom_id is set on the purchase unit when the order is created and PayPal echoes it
        /// back on capture, but which level it appears at has moved between API versions. Both
        /// known positions are checked and a miss returns null so the caller can refuse rather
        /// than assume the order belongs to whoever asked.
        /// </summary>
        private static string? TryReadCustomId(JsonElement root)
        {
            if (!root.TryGetProperty("purchase_units", out var units) || units.GetArrayLength() == 0)
                return null;

            var unit = units[0];

            if (unit.TryGetProperty("custom_id", out var direct))
                return direct.GetString();

            if (unit.TryGetProperty("payments", out var payments)
                && payments.TryGetProperty("captures", out var captures)
                && captures.GetArrayLength() > 0
                && captures[0].TryGetProperty("custom_id", out var onCapture))
            {
                return onCapture.GetString();
            }

            return null;
        }

        /// <summary>
        /// Premium was granted in four places that each set the same six fields by hand while
        /// revoking had a single implementation. That asymmetry is where a drift bug comes from
        /// so grant now has one definition too. Pass null for expiresAt on recurring, which is
        /// what HasActivePremium reads as active until PayPal says otherwise.
        /// </summary>
        private static void GrantPremium(User user, string subscriptionType, DateTime? expiresAt)
        {
            user.IsPremium = true;
            user.SubscriptionType = subscriptionType;
            user.PremiumStartedAt ??= DateTime.UtcNow;
            user.PremiumExpiresAt = expiresAt;
            user.MaxGroups = PremiumMaxGroups;
        }

        private static void RevokePremium(User user)
        {
            user.IsPremium = false;
            user.PremiumStartedAt = null;
            user.PremiumExpiresAt = null;
            user.PayPalSubscriptionId = null;
            user.PayPalOrderId = null;
            user.SubscriptionType = null;
            user.MaxGroups = FreeMaxGroups;
        }
    }
}