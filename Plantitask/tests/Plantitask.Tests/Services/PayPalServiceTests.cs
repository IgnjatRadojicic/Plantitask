using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plantitask.Core.DTO.Paypal;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
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

        /// <summary>
        /// The entitlement service shares the context because it only stages grants and
        /// PayPalService owns the save. Giving it a context of its own would leave every staged
        /// grant unsaved and every grant assertion failing for a reason that is not the code.
        /// </summary>
        private PayPalService NewSut(IApplicationDbContext context) => new(
            context,
            new HttpClient(_http),
            Options.Create(Settings),
            new MemoryCache(new MemoryCacheOptions()),
            TestServices.Entitlements(context),
            NullLogger<PayPalService>.Instance);

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        private async Task<Guid> SeedGrantAsync(
            Guid userId, string source, string payPalRef, DateTime? endsAt, DateTime? startsAt = null)
        {
            await using var db = NewContext();
            return await db.SeedGrantAsync(userId, source, payPalRef, endsAt, startsAt);
        }

        private async Task<List<UserPlanGrant>> ReadGrantsAsync(Guid userId)
        {
            await using var db = NewContext();
            return await db.UserPlanGrants
                .Where(g => g.UserId == userId)
                .OrderBy(g => g.StartsAt)
                .ToListAsync();
        }

        /// <summary>
        /// Asks the real resolver rather than restating its rule here, so these tests say what
        /// the user ends up holding and EntitlementServiceTests owns how that is worked out.
        /// </summary>
        private async Task<bool> IsPremiumAsync(Guid userId)
        {
            await using var db = NewContext();
            var result = await TestServices.Entitlements(db).GetEntitlementsAsync(userId);
            return result.Value!.IsPremium;
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
        public async Task ActivateSubscriptionAsync_OpensARecurringGrantWhenPayPalSaysActive()
        {
            await SeedAsync();

            _http.Respond($"{SubscriptionsPath}/I-SUB-1", HttpStatusCode.OK, """{"status":"ACTIVE"}""");

            await using var act = NewContext();
            var result = await NewSut(act).ActivateSubscriptionAsync(MemberId, "I-SUB-1");

            Assert.True(result.IsSuccess, result.Error?.Message);

            var grant = Assert.Single(await ReadGrantsAsync(MemberId));
            Assert.Equal(GrantSource.PayPalSubscription, grant.Source);
            Assert.Equal("I-SUB-1", grant.PayPalRef);
            Assert.Null(grant.EndsAt);
            Assert.Equal(MemberId, grant.GrantedBy);
            Assert.True(await IsPremiumAsync(MemberId));
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
            Assert.Empty(await ReadGrantsAsync(MemberId));
        }

        [Fact]
        public async Task ActivateSubscriptionAsync_IsANoOpWhenTheWebhookAlreadyGrantedTheSameSubscription()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1", endsAt: null);

            await using var act = NewContext();
            var result = await NewSut(act).ActivateSubscriptionAsync(MemberId, "I-SUB-1");

            Assert.True(result.IsSuccess);
            Assert.Equal(0, _http.CountOfRequestsTo(SubscriptionsPath));
            Assert.Single(await ReadGrantsAsync(MemberId));
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
        public async Task CaptureOrderAsync_OpensAThirtyDayGrantWhenTheOrderCompletesAndBelongsToTheBuyer()
        {
            await SeedAsync();

            _http.Respond($"{OrdersPath}/ORDER-1/capture", HttpStatusCode.Created,
                $$"""{"status":"COMPLETED","purchase_units":[{"custom_id":"{{MemberId}}"}]}""");

            await using var act = NewContext();
            var result = await NewSut(act).CaptureOrderAsync(MemberId, "ORDER-1");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.True(result.Value!.Success);

            var grant = Assert.Single(await ReadGrantsAsync(MemberId));
            Assert.Equal(GrantSource.PayPalOneTime, grant.Source);
            Assert.Equal("ORDER-1", grant.PayPalRef);
            Assert.NotNull(grant.EndsAt);
            Assert.Equal(DateTime.UtcNow.AddDays(30), grant.EndsAt!.Value, TimeSpan.FromMinutes(1));
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
            Assert.True(await IsPremiumAsync(MemberId));
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
            Assert.Empty(await ReadGrantsAsync(MemberId));
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
            Assert.Empty(await ReadGrantsAsync(MemberId));
            Assert.Empty(await ReadGrantsAsync(LeadId));
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
            Assert.Empty(await ReadGrantsAsync(MemberId));
        }

        [Fact]
        public async Task CaptureOrderAsync_IsIdempotentForAnOrderAlreadyCaptured()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime, "ORDER-1", DateTime.UtcNow.AddDays(30));

            await using var act = NewContext();
            var result = await NewSut(act).CaptureOrderAsync(MemberId, "ORDER-1");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.True(result.Value!.Success);
            Assert.Equal(0, _http.CountOfRequestsTo("/capture"));
            Assert.Single(await ReadGrantsAsync(MemberId));
        }

        /// <summary>
        /// The old check compared against PayPalOrderId on the user, which the expiry job wiped
        /// when the pass ran out, so the record that the order had been granted went with it.
        /// Grant rows are ended and never deleted, so an order paid for once stays paid for once.
        /// </summary>
        [Fact]
        public async Task CaptureOrderAsync_NeverGrantsTheSameOrderAgainAfterItsPassRanOut()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime, "ORDER-1",
                endsAt: DateTime.UtcNow.AddDays(-1), startsAt: DateTime.UtcNow.AddDays(-31));

            await using var act = NewContext();
            var result = await NewSut(act).CaptureOrderAsync(MemberId, "ORDER-1");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(0, _http.CountOfRequestsTo("/capture"));
            Assert.Single(await ReadGrantsAsync(MemberId));
            Assert.False(await IsPremiumAsync(MemberId));
        }

        [Fact]
        public async Task CancelSubscriptionAsync_EndsTheSubscriptionGrantAndRecordsWhoCancelled()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1", endsAt: null);

            _http.Respond($"{SubscriptionsPath}/I-SUB-1/cancel", HttpStatusCode.NoContent, "{}");

            await using var act = NewContext();
            var result = await NewSut(act).CancelSubscriptionAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(1, _http.CountOfRequestsTo("/I-SUB-1/cancel"));

            var grant = Assert.Single(await ReadGrantsAsync(MemberId));
            Assert.NotNull(grant.EndsAt);
            Assert.NotNull(grant.CancelledAt);
            Assert.Equal(MemberId, grant.EndedBy);
            Assert.False(await IsPremiumAsync(MemberId));
        }

        /// <summary>
        /// The local end proceeds even when PayPal's cancel call fails, so a user is never
        /// trapped in a subscription our side thinks is active. The stated cost is that PayPal
        /// may keep billing until somebody reads the warning, which is the open item in
        /// paypal-service.md K.
        /// </summary>
        [Fact]
        public async Task CancelSubscriptionAsync_EndsLocallyEvenWhenPayPalRefusesTheCancel()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1", endsAt: null);

            _http.Respond($"{SubscriptionsPath}/I-SUB-1/cancel", HttpStatusCode.InternalServerError, "{}");

            await using var act = NewContext();
            var result = await NewSut(act).CancelSubscriptionAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.False(await IsPremiumAsync(MemberId));
        }

        /// <summary>
        /// A 30 day pass has no billing agreement behind it, so there is nothing for cancel to
        /// end. It finds no subscription grant, contacts nobody and leaves the paid days alone.
        /// </summary>
        [Fact]
        public async Task CancelSubscriptionAsync_RefusesWhenTheOnlyGrantIsAPassAndKeepsThePaidDays()
        {
            await SeedAsync();
            var passEndsAt = DateTime.UtcNow.AddDays(10);
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime, "ORDER-1", passEndsAt);

            await using var act = NewContext();
            var result = await NewSut(act).CancelSubscriptionAsync(MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            Assert.Equal(0, _http.CountOfRequestsTo("/cancel"));

            var pass = Assert.Single(await ReadGrantsAsync(MemberId));
            Assert.Equal(passEndsAt, pass.EndsAt!.Value, TimeSpan.FromSeconds(1));
            Assert.Null(pass.CancelledAt);
            Assert.True(await IsPremiumAsync(MemberId));
        }

        /// <summary>
        /// The guarantee the old one time special case was protecting, now held by the schema. A
        /// pass bought during a live subscription is its own row, so cancelling the subscription
        /// ends that row only and the days paid for through the pass keep running.
        /// </summary>
        [Fact]
        public async Task CancelSubscriptionAsync_LeavesAPassBoughtAlongsideTheSubscriptionRunning()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1", endsAt: null);
            var passEndsAt = DateTime.UtcNow.AddDays(10);
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime, "ORDER-1", passEndsAt);

            _http.Respond($"{SubscriptionsPath}/I-SUB-1/cancel", HttpStatusCode.NoContent, "{}");

            await using var act = NewContext();
            var result = await NewSut(act).CancelSubscriptionAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            var grants = await ReadGrantsAsync(MemberId);
            var subscription = grants.Single(g => g.Source == GrantSource.PayPalSubscription);
            var pass = grants.Single(g => g.Source == GrantSource.PayPalOneTime);

            Assert.NotNull(subscription.CancelledAt);
            Assert.Null(pass.CancelledAt);
            Assert.Equal(passEndsAt, pass.EndsAt!.Value, TimeSpan.FromSeconds(1));
            Assert.True(await IsPremiumAsync(MemberId));
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
        public async Task GetPremiumStatusAsync_ReportsAnExpiredPassAsNoLongerPremium()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime, "ORDER-1",
                endsAt: DateTime.UtcNow.AddDays(-1), startsAt: DateTime.UtcNow.AddDays(-31));

            await using var act = NewContext();
            var result = await NewSut(act).GetPremiumStatusAsync(MemberId);

            Assert.False(result.Value!.IsPremium);
            Assert.Null(result.Value.SubscriptionType);
        }

        [Fact]
        public async Task GetPremiumStatusAsync_ReportsALiveSubscription()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1", endsAt: null);

            await using var act = NewContext();
            var result = await NewSut(act).GetPremiumStatusAsync(MemberId);

            Assert.True(result.Value!.IsPremium);
            Assert.Equal("recurring", result.Value.SubscriptionType);
            Assert.Null(result.Value.ExpiresAt);
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
            Assert.Empty(await ReadGrantsAsync(MemberId));

            await using var assert = NewContext();
            Assert.Empty(await assert.ProcessedWebhookEvents.ToListAsync());
        }

        [Fact]
        public async Task HandleWebhookAsync_TreatsAVerificationCallThatBlowsUpAsAFailedSignature()
        {
            await SeedAsync();
            _http.Throw(VerifyPath, new HttpRequestException("paypal unreachable"));

            var body = WebhookBody("EVT-1", "BILLING.SUBSCRIPTION.ACTIVATED", customId: MemberId.ToString());

            await using var act = NewContext();
            var result = await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True(result.IsFailure);
            Assert.Equal("Unauthorized", result.Error!.Code);
        }

        [Fact]
        public async Task HandleWebhookAsync_OpensARecurringGrantOnSubscriptionActivated()
        {
            await SeedAsync();
            SignatureVerifies();

            var body = WebhookBody("EVT-1", "BILLING.SUBSCRIPTION.ACTIVATED",
                resourceId: "I-SUB-1", customId: MemberId.ToString());

            await using var act = NewContext();
            var result = await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True(result.IsSuccess, result.Error?.Message);

            var grant = Assert.Single(await ReadGrantsAsync(MemberId));
            Assert.Equal(GrantSource.PayPalSubscription, grant.Source);
            Assert.Equal("I-SUB-1", grant.PayPalRef);
            Assert.Null(grant.EndsAt);
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
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1", endsAt: null);

            await using (var first = NewContext())
                await NewSut(first).HandleWebhookAsync(body, WebhookHeaders());

            Assert.False(await IsPremiumAsync(MemberId));

            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1", endsAt: null);

            await using (var second = NewContext())
                await NewSut(second).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True(await IsPremiumAsync(MemberId));

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

        /// <summary>
        /// EndedBy stays null because PayPal ended these and no person did, which is how the
        /// history tells a webhook ending apart from a user pressing cancel.
        /// </summary>
        [Theory]
        [InlineData("BILLING.SUBSCRIPTION.CANCELLED")]
        [InlineData("BILLING.SUBSCRIPTION.SUSPENDED")]
        [InlineData("BILLING.SUBSCRIPTION.EXPIRED")]
        public async Task HandleWebhookAsync_EndsTheGrantOnEveryEndOfSubscriptionEvent(string eventType)
        {
            await SeedAsync();
            SignatureVerifies();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1", endsAt: null);

            var body = WebhookBody("EVT-1", eventType, resourceId: "I-SUB-1");

            await using var act = NewContext();
            await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            var grant = Assert.Single(await ReadGrantsAsync(MemberId));
            Assert.NotNull(grant.EndsAt);
            Assert.NotNull(grant.CancelledAt);
            Assert.Null(grant.EndedBy);
            Assert.False(await IsPremiumAsync(MemberId));
        }

        /// <summary>
        /// A failed charge does not end anything. PayPal retries for several days and only sends
        /// one of the ending events once it gives up, so ending on the first bounce would take
        /// premium from anyone whose card expired even though the retry usually succeeds.
        /// </summary>
        [Fact]
        public async Task HandleWebhookAsync_DoesNotEndTheGrantOnASinglyFailedPayment()
        {
            await SeedAsync();
            SignatureVerifies();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1", endsAt: null);

            var body = WebhookBody("EVT-1", "BILLING.SUBSCRIPTION.PAYMENT.FAILED", resourceId: "I-SUB-1");

            await using var act = NewContext();
            await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.Null(Assert.Single(await ReadGrantsAsync(MemberId)).EndsAt);
        }

        /// <summary>
        /// An open grant has no end date to push out, so a monthly charge on a live subscription
        /// has nothing to write.
        /// </summary>
        [Fact]
        public async Task HandleWebhookAsync_AChargeOnALiveSubscriptionChangesNothing()
        {
            await SeedAsync();
            SignatureVerifies();
            var grantId = await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1", endsAt: null);

            var body = WebhookBody("EVT-1", "PAYMENT.SALE.COMPLETED", billingAgreementId: "I-SUB-1");

            await using var act = NewContext();
            var result = await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            Assert.True(result.IsSuccess, result.Error?.Message);

            var grant = Assert.Single(await ReadGrantsAsync(MemberId));
            Assert.Equal(grantId, grant.Id);
            Assert.Null(grant.EndsAt);
        }

        /// <summary>
        /// A charge against a grant already closed means PayPal resumed billing after a
        /// suspension. That opens a new grant and leaves the closed one as history rather than
        /// rewriting it.
        /// </summary>
        [Fact]
        public async Task HandleWebhookAsync_AChargeAfterASuspensionOpensANewGrantAndKeepsTheOldOne()
        {
            await SeedAsync();
            SignatureVerifies();
            var closedEndsAt = DateTime.UtcNow.AddDays(-5);
            var closedId = await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1",
                endsAt: closedEndsAt, startsAt: DateTime.UtcNow.AddDays(-40));

            var body = WebhookBody("EVT-1", "PAYMENT.SALE.COMPLETED", billingAgreementId: "I-SUB-1");

            await using var act = NewContext();
            await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            var grants = await ReadGrantsAsync(MemberId);
            Assert.Equal(2, grants.Count);

            var closed = grants.Single(g => g.Id == closedId);
            Assert.Equal(closedEndsAt, closed.EndsAt!.Value, TimeSpan.FromSeconds(1));

            var reopened = grants.Single(g => g.Id != closedId);
            Assert.Null(reopened.EndsAt);
            Assert.Equal("I-SUB-1", reopened.PayPalRef);
            Assert.True(await IsPremiumAsync(MemberId));
        }

        /// <summary>
        /// The safety net for one time orders. Without it the browser capture is the only path,
        /// so a user who pays and closes the tab before the redirect gets nothing while PayPal
        /// has their money.
        /// </summary>
        [Fact]
        public async Task HandleWebhookAsync_GrantsAPassWhenTheBrowserNeverCameBack()
        {
            await SeedAsync();
            SignatureVerifies();

            var body = WebhookBody("EVT-1", "PAYMENT.CAPTURE.COMPLETED",
                resourceId: "ORDER-1", customId: MemberId.ToString());

            await using var act = NewContext();
            await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            var grant = Assert.Single(await ReadGrantsAsync(MemberId));
            Assert.Equal(GrantSource.PayPalOneTime, grant.Source);
            Assert.Equal("ORDER-1", grant.PayPalRef);
            Assert.NotNull(grant.EndsAt);
        }

        [Fact]
        public async Task HandleWebhookAsync_DoesNotExtendAnOrderTheBrowserCaptureAlreadyGranted()
        {
            await SeedAsync();
            SignatureVerifies();

            var alreadyEndsAt = DateTime.UtcNow.AddDays(30);
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime, "ORDER-1", alreadyEndsAt);

            var body = WebhookBody("EVT-1", "PAYMENT.CAPTURE.COMPLETED",
                resourceId: "ORDER-1", customId: MemberId.ToString());

            await using var act = NewContext();
            await NewSut(act).HandleWebhookAsync(body, WebhookHeaders());

            var grant = Assert.Single(await ReadGrantsAsync(MemberId));
            Assert.Equal(alreadyEndsAt, grant.EndsAt!.Value, TimeSpan.FromSeconds(1));
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
            Assert.Empty(await ReadGrantsAsync(MemberId));
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
            Assert.Empty(await ReadGrantsAsync(MemberId));

            await using var assert = NewContext();
            Assert.Single(await assert.ProcessedWebhookEvents.ToListAsync());
        }
    }
}
