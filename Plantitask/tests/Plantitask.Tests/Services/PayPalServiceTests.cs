using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plantitask.Core.Constants;
using Plantitask.Core.DTO.Paypal;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class PayPalServiceTests : DbTestBase
    {
        private const string TokenPath = "/v1/oauth2/token";
        private const string VerifyPath = "/v1/notifications/verify-webhook-signature";
        private const string SubscriptionsPath = "/v1/billing/subscriptions";
        private const string OrdersPath = "/v2/checkout/orders";

        private static readonly PayPalSettings Settings = new()
        {
            ClientId = "client-id",
            ClientSecret = "client-secret",
            BaseUrl = "https://api-m.sandbox.paypal.example",
            RecurringPlanId = "P-PLAN-1",
            OneTimePrice = 4.99m,
            Currency = "USD",
            WebhookId = "WH-1"
        };

        private readonly StubHttpMessageHandler _http = new();

        public PayPalServiceTests(PostgresFixture fixture) : base(fixture)
        {
            _http.Respond(TokenPath, HttpStatusCode.OK,
                """{"access_token":"tok-123","expires_in":32400}""");
        }

        private PayPalService NewSut(IApplicationDbContext context) => new(
            context,
            new HttpClient(_http),
            Options.Create(Settings),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<PayPalService>.Instance);

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        private async Task<User> ReadUserAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.Users.SingleAsync(u => u.Id == id);
        }

        private async Task GrantPremiumAsync(
            Guid userId, string type, DateTime? expiresAt, string? orderId = null, string? subscriptionId = null)
        {
            await using var db = NewContext();
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.IsPremium = true;
            user.SubscriptionType = type;
            user.PremiumStartedAt = DateTime.UtcNow.AddDays(-1);
            user.PremiumExpiresAt = expiresAt;
            user.PayPalOrderId = orderId;
            user.PayPalSubscriptionId = subscriptionId;
            user.MaxGroups = PlanLimits.PremiumMaxGroups;
            await db.SaveChangesAsync();
        }

        private static string WebhookBody(
            string eventId,
            string eventType,
            string resourceId = "RES-1",
            string? customId = null,
            string? billingAgreementId = null)
        {
            var resource = new Dictionary<string, object?>
            {
                ["id"] = resourceId,
                ["status"] = "COMPLETED",
                ["custom_id"] = customId,
                ["billing_agreement_id"] = billingAgreementId
            };

            return JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = eventId,
                ["event_type"] = eventType,
                ["summary"] = "a summary",
                ["resource"] = resource
            });
        }

        private static Dictionary<string, string> WebhookHeaders() => new()
        {
            ["PAYPAL-AUTH-ALGO"] = "SHA256withRSA",
            ["PAYPAL-CERT-URL"] = "https://api.paypal.com/cert.pem",
            ["PAYPAL-TRANSMISSION-ID"] = "TR-1",
            ["PAYPAL-TRANSMISSION-SIG"] = "sig",
            ["PAYPAL-TRANSMISSION-TIME"] = "2026-08-15T00:00:00Z"
        };

        private void SignatureVerifies() =>
            _http.Respond(VerifyPath, HttpStatusCode.OK, """{"verification_status":"SUCCESS"}""");

        private void SignatureFails() =>
            _http.Respond(VerifyPath, HttpStatusCode.OK, """{"verification_status":"FAILURE"}""");

        [Fact]
        public async Task CreateSubscriptionAsync_StampsTheBuyersIdIntoCustomIdAndReturnsTheApprovalUrl()
        {
            await SeedAsync();

            _http.Respond(SubscriptionsPath, HttpStatusCode.Created,
                """{"id":"I-SUB-1","links":[{"rel":"self","href":"https://x/self"},{"rel":"approve","href":"https://paypal/approve/1"}]}""");

            await using var act = NewContext();
            var result = await NewSut(act).CreateSubscriptionAsync(
                MemberId, new CreateSubscriptionRequest { ReturnUrl = "https://app/ok", CancelUrl = "https://app/no" });

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("I-SUB-1", result.Value!.SubscriptionId);
            Assert.Equal("https://paypal/approve/1", result.Value.ApprovalUrl);

            var sent = _http.RequestTo(SubscriptionsPath);
            Assert.Contains($"\"custom_id\":\"{MemberId}\"", sent.Body);
            Assert.Contains("\"plan_id\":\"P-PLAN-1\"", sent.Body);
            Assert.Equal("Bearer", sent.AuthScheme);
            Assert.Equal("tok-123", sent.AuthParameter);
        }

        [Fact]
        public async Task CreateSubscriptionAsync_ReturnsBadRequestWhenPayPalRefuses()
        {
            await SeedAsync();

            _http.Respond(SubscriptionsPath, HttpStatusCode.UnprocessableEntity, """{"name":"INVALID"}""");

            await using var act = NewContext();
            var result = await NewSut(act).CreateSubscriptionAsync(MemberId, new CreateSubscriptionRequest());

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        /// <summary>
        /// The price and currency are read from configuration rather than from the client, so a
        /// caller cannot choose what premium costs them.
        /// </summary>
        [Fact]
        public async Task CreateOneTimeOrderAsync_TakesThePriceFromConfigurationAndStampsTheBuyer()
        {
            await SeedAsync();

            _http.Respond(OrdersPath, HttpStatusCode.Created,
                """{"id":"ORDER-1","links":[{"rel":"approve","href":"https://paypal/approve/order"}]}""");

            await using var act = NewContext();
            var result = await NewSut(act).CreateOneTimeOrderAsync(
                MemberId, new CreateOrderRequest { ReturnUrl = "https://app/ok", CancelUrl = "https://app/no" });

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("ORDER-1", result.Value!.OrderId);

            var sent = _http.RequestTo(OrdersPath);
            Assert.Contains("\"value\":\"4.99\"", sent.Body);
            Assert.Contains("\"currency_code\":\"USD\"", sent.Body);
            Assert.Contains($"\"custom_id\":\"{MemberId}\"", sent.Body);
        }

        /// <summary>
        /// The access token is cached across calls because PayPal issues them with a nine hour
        /// life and every checkout was otherwise paying for an extra round trip.
        /// </summary>
        [Fact]
        public async Task TheAccessTokenIsFetchedOncePerServiceRatherThanPerCall()
        {
            await SeedAsync();

            _http.Respond(OrdersPath, HttpStatusCode.Created,
                """{"id":"ORDER-1","links":[{"rel":"approve","href":"https://paypal/approve/order"}]}""");

            await using var act = NewContext();
            var sut = NewSut(act);

            await sut.CreateOneTimeOrderAsync(MemberId, new CreateOrderRequest());
            await sut.CreateOneTimeOrderAsync(MemberId, new CreateOrderRequest());
            await sut.CreateOneTimeOrderAsync(MemberId, new CreateOrderRequest());

            Assert.Equal(1, _http.CountOfRequestsTo(TokenPath));
            Assert.Equal(3, _http.CountOfRequestsTo(OrdersPath));
        }

        [Fact]
        public async Task ActivateSubscriptionAsync_GrantsRecurringPremiumWhenPayPalSaysActive()
        {
            await SeedAsync();

            _http.Respond($"{SubscriptionsPath}/I-SUB-1", HttpStatusCode.OK, """{"status":"ACTIVE"}""");

            await using var act = NewContext();
            var result = await NewSut(act).ActivateSubscriptionAsync(MemberId, "I-SUB-1");

            Assert.True(result.IsSuccess, result.Error?.Message);

            var user = await ReadUserAsync(MemberId);
            Assert.True(user.IsPremium);
            Assert.Equal("recurring", user.SubscriptionType);
            Assert.Null(user.PremiumExpiresAt);
            Assert.Equal("I-SUB-1", user.PayPalSubscriptionId);
            Assert.Equal(PlanLimits.PremiumMaxGroups, user.MaxGroups);
        }

        /// <summary>
        /// The redirect back from PayPal proves nothing, so the real status is fetched. Anything
        /// other than ACTIVE grants nothing.
        /// </summary>
        [Theory]
        [InlineData("APPROVAL_PENDING")]
        [InlineData("SUSPENDED")]
        [InlineData("CANCELLED")]
        public async Task ActivateSubscriptionAsync_GrantsNothingUnlessPayPalSaysActive(string status)
        {
            await SeedAsync();

            _http.Respond($"{SubscriptionsPath}/I-SUB-1", HttpStatusCode.OK, $$"""{"status":"{{status}}"}""");

            await using var act = NewContext();
            var result = await NewSut(act).ActivateSubscriptionAsync(MemberId, "I-SUB-1");

            Assert.True(result.IsFailure);
            Assert.False((await ReadUserAsync(MemberId)).IsPremium);
        }

        [Fact]
        public async Task ActivateSubscriptionAsync_IsANoOpWhenTheWebhookAlreadyGrantedTheSameSubscription()
        {
            await SeedAsync();
            await GrantPremiumAsync(MemberId, "recurring", expiresAt: null, subscriptionId: "I-SUB-1");

            await using var act = NewContext();
            var result = await NewSut(act).ActivateSubscriptionAsync(MemberId, "I-SUB-1");

            Assert.True(result.IsSuccess);
            Assert.Equal(0, _http.CountOfRequestsTo(SubscriptionsPath));
        }

        [Fact]
        public async Task ActivateSubscriptionAsync_ReturnsNotFoundForAUserWhoDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).ActivateSubscriptionAsync(Guid.NewGuid(), "I-SUB-1");

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task CaptureOrderAsync_GrantsThirtyDaysWhenTheOrderCompletesAndBelongsToTheBuyer()
        {
            await SeedAsync();

            _http.Respond($"{OrdersPath}/ORDER-1/capture", HttpStatusCode.Created,
                $$"""{"status":"COMPLETED","purchase_units":[{"custom_id":"{{MemberId}}"}]}""");

            await using var act = NewContext();
            var result = await NewSut(act).CaptureOrderAsync(MemberId, "ORDER-1");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.True(result.Value!.Success);

            var user = await ReadUserAsync(MemberId);
            Assert.True(user.IsPremium);
            Assert.Equal("onetime", user.SubscriptionType);
            Assert.Equal("ORDER-1", user.PayPalOrderId);
            Assert.Equal(PlanLimits.PremiumMaxGroups, user.MaxGroups);
            Assert.NotNull(user.PremiumExpiresAt);
            Assert.Equal(DateTime.UtcNow.AddDays(30), user.PremiumExpiresAt!.Value, TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// custom_id has moved between api versions so both known positions are read. The nested
        /// one is what a v2 capture response actually returns.
        /// </summary>
        [Fact]
        public async Task CaptureOrderAsync_FindsTheOwnerStampNestedUnderTheCapture()
        {
            await SeedAsync();

            _http.Respond($"{OrdersPath}/ORDER-1/capture", HttpStatusCode.Created,
                """{"status":"COMPLETED","purchase_units":[{"payments":{"captures":[{"custom_id":"OWNER"}]}}]}"""
                    .Replace("OWNER", MemberId.ToString()));

            await using var act = NewContext();
            var result = await NewSut(act).CaptureOrderAsync(MemberId, "ORDER-1");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.True((await ReadUserAsync(MemberId)).IsPremium);
        }

        /// <summary>
        /// A capture response that cannot prove ownership grants nothing. Anything else would let
        /// somebody holding another person's approved order id capture that payment onto their
        /// own account.
        /// </summary>
        [Theory]
        [InlineData("""{"status":"COMPLETED","purchase_units":[{}]}""")]
        [InlineData("""{"status":"COMPLETED","purchase_units":[]}""")]
        [InlineData("""{"status":"COMPLETED"}""")]
        public async Task CaptureOrderAsync_FailsClosedWhenTheResponseCarriesNoOwnerStamp(string response)
        {
            await SeedAsync();

            _http.Respond($"{OrdersPath}/ORDER-1/capture", HttpStatusCode.Created, response);

            await using var act = NewContext();
            var result = await NewSut(act).CaptureOrderAsync(MemberId, "ORDER-1");

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            Assert.False((await ReadUserAsync(MemberId)).IsPremium);
        }

        [Fact]
        public async Task CaptureOrderAsync_RefusesAnOrderStampedWithSomebodyElsesId()
        {
            await SeedAsync();

            _http.Respond($"{OrdersPath}/ORDER-1/capture", HttpStatusCode.Created,
                $$"""{"status":"COMPLETED","purchase_units":[{"custom_id":"{{LeadId}}"}]}""");

            await using var act = NewContext();
            var result = await NewSut(act).CaptureOrderAsync(MemberId, "ORDER-1");

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
            Assert.False((await ReadUserAsync(MemberId)).IsPremium);
            Assert.False((await ReadUserAsync(LeadId)).IsPremium);
        }

        [Fact]
        public async Task CaptureOrderAsync_ReportsAnIncompleteCaptureWithoutGrantingAnything()
        {
            await SeedAsync();

            _http.Respond($"{OrdersPath}/ORDER-1/capture", HttpStatusCode.Created,
                """{"status":"PENDING","purchase_units":[{"custom_id":"whoever"}]}""");

            await using var act = NewContext();
            var result = await NewSut(act).CaptureOrderAsync(MemberId, "ORDER-1");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.False(result.Value!.Success);
            Assert.Equal("PENDING", result.Value.Status);
            Assert.False((await ReadUserAsync(MemberId)).IsPremium);
        }

        [Fact]
        public async Task CaptureOrderAsync_IsIdempotentForAnOrderAlreadyCaptured()
        {
            await SeedAsync();
            await GrantPremiumAsync(MemberId, "onetime", DateTime.UtcNow.AddDays(30), orderId: "ORDER-1");

            await using var act = NewContext();
            var result = await NewSut(act).CaptureOrderAsync(MemberId, "ORDER-1");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.True(result.Value!.Success);
            Assert.Equal(0, _http.CountOfRequestsTo("/capture"));
        }

        [Fact]
        public async Task CancelSubscriptionAsync_RevokesEveryPremiumField()
        {
            await SeedAsync();
            await GrantPremiumAsync(MemberId, "recurring", expiresAt: null, subscriptionId: "I-SUB-1");

            _http.Respond($"{SubscriptionsPath}/I-SUB-1/cancel", HttpStatusCode.NoContent, "{}");

            await using var act = NewContext();
            var result = await NewSut(act).CancelSubscriptionAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            var user = await ReadUserAsync(MemberId);
            Assert.False(user.IsPremium);
            Assert.Null(user.SubscriptionType);
            Assert.Null(user.PayPalSubscriptionId);
            Assert.Null(user.PremiumExpiresAt);
            Assert.Equal(PlanLimits.FreeMaxGroups, user.MaxGroups);
        }

        /// <summary>
        /// The local revoke proceeds even when PayPal's cancel call fails, so a user is never
        /// trapped in a subscription our side thinks is active. The stated cost is that PayPal
        /// may keep billing until somebody reads the warning, which is the open item in
        /// paypal-service.md K.
        /// </summary>
        [Fact]
        public async Task CancelSubscriptionAsync_RevokesLocallyEvenWhenPayPalRefusesTheCancel()
        {
            await SeedAsync();
            await GrantPremiumAsync(MemberId, "recurring", expiresAt: null, subscriptionId: "I-SUB-1");

            _http.Respond($"{SubscriptionsPath}/I-SUB-1/cancel", HttpStatusCode.InternalServerError, "{}");

            await using var act = NewContext();
            var result = await NewSut(act).CancelSubscriptionAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.False((await ReadUserAsync(MemberId)).IsPremium);
        }

        /// <summary>
        /// A 30 day pass has no billing agreement behind it, so cancelling it contacts nobody and
        /// the only thing a revoke could accomplish is deleting days the user already paid for.
        /// Refusing is the fix, and the row must come back untouched.
        /// </summary>
        [Fact]
        public async Task CancelSubscriptionAsync_RefusesAOneTimePassAndKeepsThePaidDays()
        {
            await SeedAsync();
            await GrantPremiumAsync(MemberId, "onetime", DateTime.UtcNow.AddDays(10), orderId: "ORDER-1");

            await using var act = NewContext();
            var result = await NewSut(act).CancelSubscriptionAsync(MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            Assert.Equal(0, _http.CountOfRequestsTo("/cancel"));

            var user = await ReadUserAsync(MemberId);
            Assert.True(user.IsPremium);
            Assert.Equal("onetime", user.SubscriptionType);
            Assert.True(user.PremiumExpiresAt > DateTime.UtcNow);
        }

        [Fact]
        public async Task CancelSubscriptionAsync_RejectsAUserWithNoPremium()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).CancelSubscriptionAsync(MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        [Fact]
        public async Task GetPremiumStatusAsync_ReportsAnExpiredOneTimeAsNoLongerPremium()
        {
            await SeedAsync();
            await GrantPremiumAsync(MemberId, "onetime", DateTime.UtcNow.AddDays(-1), orderId: "ORDER-1");

            await using var act = NewContext();
            var result = await NewSut(act).GetPremiumStatusAsync(MemberId);

            Assert.False(result.Value!.IsPremium);
            Assert.False(result.Value.CanUseDarkMode);
            Assert.Equal(PlanLimits.FreeMaxGroups, result.Value.MaxGroups);
        }

        [Fact]
        public async Task GetPremiumStatusAsync_ReportsALivePremiumWithItsLimits()
        {
            await SeedAsync();
            await GrantPremiumAsync(MemberId, "recurring", expiresAt: null, subscriptionId: "I-SUB-1");

            await using var act = NewContext();
            var result = await NewSut(act).GetPremiumStatusAsync(MemberId);

            Assert.True(result.Value!.IsPremium);
            Assert.True(result.Value.CanUseDarkMode);
            Assert.Equal(PlanLimits.PremiumMaxGroups, result.Value.MaxGroups);
            Assert.Equal("recurring", result.Value.SubscriptionType);
        }

        /// <summary>
        /// An unverifiable webhook is treated as forged. Nothing is applied and no processed
        /// marker is written, so a genuine redelivery is still accepted later.
        /// </summary>
        [Fact]
        public async Task HandleWebhookAsync_RejectsAnEventWhoseSignatureDoesNotVerify()
        {
            await SeedAsync();
            SignatureFails();

            var body = WebhookBody("EVT-1", "BILLING.SUBSCRIPTION.ACTIVATED", customId: MemberId.ToString());

            await using var act = NewContext();
            var result = await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True(result.IsFailure);
            Assert.Equal("Unauthorized", result.Error!.Code);
            Assert.False((await ReadUserAsync(MemberId)).IsPremium);

            await using var assert = NewContext();
            Assert.Empty(await assert.ProcessedWebhookEvents.ToListAsync());
        }

        [Fact]
        public async Task HandleWebhookAsync_TreatsAVerificationCallThatBlowsUpAsAFailedSignature()
        {
            await SeedAsync();
            _http.Throw(VerifyPath, new HttpRequestMessage().Content is null
                ? new HttpRequestException("paypal unreachable")
                : new HttpRequestException("paypal unreachable"));

            var body = WebhookBody("EVT-1", "BILLING.SUBSCRIPTION.ACTIVATED", customId: MemberId.ToString());

            await using var act = NewContext();
            var result = await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True(result.IsFailure);
            Assert.Equal("Unauthorized", result.Error!.Code);
        }

        [Fact]
        public async Task HandleWebhookAsync_GrantsRecurringPremiumOnSubscriptionActivated()
        {
            await SeedAsync();
            SignatureVerifies();

            var body = WebhookBody("EVT-1", "BILLING.SUBSCRIPTION.ACTIVATED",
                resourceId: "I-SUB-1", customId: MemberId.ToString());

            await using var act = NewContext();
            var result = await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True(result.IsSuccess, result.Error?.Message);

            var user = await ReadUserAsync(MemberId);
            Assert.True(user.IsPremium);
            Assert.Equal("recurring", user.SubscriptionType);
            Assert.Equal("I-SUB-1", user.PayPalSubscriptionId);
        }

        /// <summary>
        /// PayPal redelivers, so the event id table is what stops a second copy being applied.
        /// Keying on the id keeps a handler safe even if somebody later writes one that is not
        /// naturally repeatable.
        /// </summary>
        [Fact]
        public async Task HandleWebhookAsync_IgnoresAnEventItHasAlreadyProcessed()
        {
            await SeedAsync();
            SignatureVerifies();

            var body = WebhookBody("EVT-1", "BILLING.SUBSCRIPTION.CANCELLED", resourceId: "I-SUB-1");
            await GrantPremiumAsync(MemberId, "recurring", expiresAt: null, subscriptionId: "I-SUB-1");

            await using (var first = NewContext())
                await NewSut(first).HandleWebhookAsync(body, WebhookHeaders());

            Assert.False((await ReadUserAsync(MemberId)).IsPremium);

            await GrantPremiumAsync(MemberId, "recurring", expiresAt: null, subscriptionId: "I-SUB-1");

            await using (var second = NewContext())
                await NewSut(second).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True((await ReadUserAsync(MemberId)).IsPremium);

            await using var assert = NewContext();
            Assert.Single(await assert.ProcessedWebhookEvents.ToListAsync());
        }

        [Fact]
        public async Task HandleWebhookAsync_RecordsTheEventIdAndTypeItProcessed()
        {
            await SeedAsync();
            SignatureVerifies();

            var body = WebhookBody("EVT-1", "BILLING.SUBSCRIPTION.ACTIVATED", customId: MemberId.ToString());

            await using var act = NewContext();
            await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            await using var assert = NewContext();
            var processed = Assert.Single(await assert.ProcessedWebhookEvents.ToListAsync());
            Assert.Equal("EVT-1", processed.EventId);
            Assert.Equal("BILLING.SUBSCRIPTION.ACTIVATED", processed.EventType);
        }

        [Theory]
        [InlineData("BILLING.SUBSCRIPTION.CANCELLED")]
        [InlineData("BILLING.SUBSCRIPTION.SUSPENDED")]
        [InlineData("BILLING.SUBSCRIPTION.EXPIRED")]
        public async Task HandleWebhookAsync_RevokesOnEveryEndOfSubscriptionEvent(string eventType)
        {
            await SeedAsync();
            SignatureVerifies();
            await GrantPremiumAsync(MemberId, "recurring", expiresAt: null, subscriptionId: "I-SUB-1");

            var body = WebhookBody("EVT-1", eventType, resourceId: "I-SUB-1");

            await using var act = NewContext();
            await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            var user = await ReadUserAsync(MemberId);
            Assert.False(user.IsPremium);
            Assert.Equal(PlanLimits.FreeMaxGroups, user.MaxGroups);
        }

        /// <summary>
        /// A failed charge does not revoke. PayPal retries for several days and only sends one of
        /// the ending events once it gives up, so revoking on the first bounce would take premium
        /// from anyone whose card expired even though the retry usually succeeds.
        /// </summary>
        [Fact]
        public async Task HandleWebhookAsync_DoesNotRevokeOnASinglyFailedPayment()
        {
            await SeedAsync();
            SignatureVerifies();
            await GrantPremiumAsync(MemberId, "recurring", expiresAt: null, subscriptionId: "I-SUB-1");

            var body = WebhookBody("EVT-1", "BILLING.SUBSCRIPTION.PAYMENT.FAILED", resourceId: "I-SUB-1");

            await using var act = NewContext();
            await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True((await ReadUserAsync(MemberId)).IsPremium);
        }

        [Fact]
        public async Task HandleWebhookAsync_RefreshesPremiumOnASuccessfulRecurringCharge()
        {
            await SeedAsync();
            SignatureVerifies();
            await GrantPremiumAsync(MemberId, "recurring", expiresAt: null, subscriptionId: "I-SUB-1");

            var body = WebhookBody("EVT-1", "PAYMENT.SALE.COMPLETED", billingAgreementId: "I-SUB-1");

            await using var act = NewContext();
            var result = await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True(result.IsSuccess, result.Error?.Message);

            var user = await ReadUserAsync(MemberId);
            Assert.True(user.IsPremium);
            Assert.Equal("recurring", user.SubscriptionType);
        }

        /// <summary>
        /// The safety net for one time orders. Without it the browser capture is the only path,
        /// so a user who pays and closes the tab before the redirect gets nothing while PayPal
        /// has their money.
        /// </summary>
        [Fact]
        public async Task HandleWebhookAsync_GrantsOneTimePremiumWhenTheBrowserNeverCameBack()
        {
            await SeedAsync();
            SignatureVerifies();

            var body = WebhookBody("EVT-1", "PAYMENT.CAPTURE.COMPLETED",
                resourceId: "ORDER-1", customId: MemberId.ToString());

            await using var act = NewContext();
            await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            var user = await ReadUserAsync(MemberId);
            Assert.True(user.IsPremium);
            Assert.Equal("onetime", user.SubscriptionType);
            Assert.Equal("ORDER-1", user.PayPalOrderId);
            Assert.NotNull(user.PremiumExpiresAt);
        }

        [Fact]
        public async Task HandleWebhookAsync_DoesNotExtendAnOrderTheBrowserCaptureAlreadyGranted()
        {
            await SeedAsync();
            SignatureVerifies();

            var alreadyExpiresAt = DateTime.UtcNow.AddDays(30);
            await GrantPremiumAsync(MemberId, "onetime", alreadyExpiresAt, orderId: "ORDER-1");

            var body = WebhookBody("EVT-1", "PAYMENT.CAPTURE.COMPLETED",
                resourceId: "ORDER-1", customId: MemberId.ToString());

            await using var act = NewContext();
            await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            var user = await ReadUserAsync(MemberId);
            Assert.Equal(alreadyExpiresAt, user.PremiumExpiresAt!.Value, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task HandleWebhookAsync_GrantsNothingForACaptureEventWithNoUsableOwnerStamp()
        {
            await SeedAsync();
            SignatureVerifies();

            var body = WebhookBody("EVT-1", "PAYMENT.CAPTURE.COMPLETED",
                resourceId: "ORDER-1", customId: "not-a-guid");

            await using var act = NewContext();
            var result = await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.False((await ReadUserAsync(MemberId)).IsPremium);
        }

        [Fact]
        public async Task HandleWebhookAsync_RejectsAnEventWithNoId()
        {
            await SeedAsync();
            SignatureVerifies();

            var body = WebhookBody("", "BILLING.SUBSCRIPTION.ACTIVATED", customId: MemberId.ToString());

            await using var act = NewContext();
            var result = await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        [Fact]
        public async Task HandleWebhookAsync_AcceptsAnEventTypeItDoesNotHandleWithoutChangingAnything()
        {
            await SeedAsync();
            SignatureVerifies();

            var body = WebhookBody("EVT-1", "CHECKOUT.ORDER.APPROVED", customId: MemberId.ToString());

            await using var act = NewContext();
            var result = await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.False((await ReadUserAsync(MemberId)).IsPremium);

            await using var assert = NewContext();
            Assert.Single(await assert.ProcessedWebhookEvents.ToListAsync());
        }
    }
}
