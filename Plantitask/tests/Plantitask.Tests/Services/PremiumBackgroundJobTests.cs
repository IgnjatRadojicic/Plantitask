using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Plantitask.Core.Constants;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;

namespace Plantitask.Tests.Services
{
    public class PremiumBackgroundJobTests : DbTestBase
    {
        public PremiumBackgroundJobTests(PostgresFixture fixture) : base(fixture) { }

        private PremiumBackgroundJob NewSut(IApplicationDbContext context) => new(
            context, NullLogger<PremiumBackgroundJob>.Instance);

        private async Task<Guid> SeedUserAsync(
            string name,
            bool isPremium,
            string? subscriptionType,
            DateTime? expiresAt,
            int maxGroups = PlanLimits.PremiumMaxGroups,
            string? payPalOrderId = "ORDER-1",
            string? payPalSubscriptionId = null)
        {
            await using var db = NewContext();

            var user = TestData.User(Guid.NewGuid(), name);
            user.IsPremium = isPremium;
            user.SubscriptionType = subscriptionType;
            user.PremiumExpiresAt = expiresAt;
            user.PremiumStartedAt = expiresAt?.AddDays(-30);
            user.MaxGroups = maxGroups;
            user.PayPalOrderId = payPalOrderId;
            user.PayPalSubscriptionId = payPalSubscriptionId;

            db.Users.Add(user);
            await db.SaveChangesAsync();

            return user.Id;
        }

        private async Task<User> ReadUserAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.Users.SingleAsync(u => u.Id == id);
        }

        [Fact]
        public async Task ExpireOneTimePremiumAsync_ClearsEveryPremiumFieldOnAnExpiredOneTimeUser()
        {
            var userId = await SeedUserAsync(
                "expired", isPremium: true, subscriptionType: "onetime",
                expiresAt: DateTime.UtcNow.AddDays(-1));

            await using var act = NewContext();
            await NewSut(act).ExpireOneTimePremiumAsync();

            var user = await ReadUserAsync(userId);

            Assert.False(user.IsPremium);
            Assert.Null(user.SubscriptionType);
            Assert.Null(user.PremiumStartedAt);
            Assert.Null(user.PremiumExpiresAt);
            Assert.Null(user.PayPalOrderId);
            Assert.Null(user.PayPalSubscriptionId);
            Assert.Equal(PlanLimits.FreeMaxGroups, user.MaxGroups);
        }

        /// <summary>
        /// MaxGroups is denormalised, so leaving it at the premium value is the part that
        /// actually leaks. HasActivePremium would still compute the right answer for feature
        /// checks while anything reading the column directly kept seeing ten.
        /// </summary>
        [Fact]
        public async Task ExpireOneTimePremiumAsync_PutsTheGroupLimitBackToTheFreePlan()
        {
            var userId = await SeedUserAsync(
                "expired", isPremium: true, subscriptionType: "onetime",
                expiresAt: DateTime.UtcNow.AddDays(-1), maxGroups: PlanLimits.PremiumMaxGroups);

            await using var act = NewContext();
            await NewSut(act).ExpireOneTimePremiumAsync();

            Assert.Equal(PlanLimits.FreeMaxGroups, (await ReadUserAsync(userId)).MaxGroups);
        }

        [Fact]
        public async Task ExpireOneTimePremiumAsync_StampsUpdatedAtByHand()
        {
            var userId = await SeedUserAsync(
                "expired", isPremium: true, subscriptionType: "onetime",
                expiresAt: DateTime.UtcNow.AddDays(-1));

            Assert.Null((await ReadUserAsync(userId)).UpdatedAt);

            await using var act = NewContext();
            await NewSut(act).ExpireOneTimePremiumAsync();

            Assert.NotNull((await ReadUserAsync(userId)).UpdatedAt);
        }

        [Fact]
        public async Task ExpireOneTimePremiumAsync_LeavesAOneTimeUserWhoseTimeHasNotRunOut()
        {
            var userId = await SeedUserAsync(
                "still-paid", isPremium: true, subscriptionType: "onetime",
                expiresAt: DateTime.UtcNow.AddDays(10));

            await using var act = NewContext();
            await NewSut(act).ExpireOneTimePremiumAsync();

            var user = await ReadUserAsync(userId);

            Assert.True(user.IsPremium);
            Assert.Equal("onetime", user.SubscriptionType);
            Assert.Equal(PlanLimits.PremiumMaxGroups, user.MaxGroups);
        }

        /// <summary>
        /// A null PremiumExpiresAt means PayPal is still charging on a recurring plan, and only a
        /// webhook may end one of those. Expiring it here would cancel a subscription the user is
        /// still paying for.
        /// </summary>
        [Fact]
        public async Task ExpireOneTimePremiumAsync_NeverTouchesARecurringSubscription()
        {
            var userId = await SeedUserAsync(
                "subscriber", isPremium: true, subscriptionType: "subscription",
                expiresAt: null, payPalOrderId: null, payPalSubscriptionId: "SUB-1");

            await using var act = NewContext();
            await NewSut(act).ExpireOneTimePremiumAsync();

            var user = await ReadUserAsync(userId);

            Assert.True(user.IsPremium);
            Assert.Equal("subscription", user.SubscriptionType);
            Assert.Equal("SUB-1", user.PayPalSubscriptionId);
            Assert.Equal(PlanLimits.PremiumMaxGroups, user.MaxGroups);
        }

        /// <summary>
        /// A recurring subscription that also carries a past expiry date is still off limits.
        /// The subscription type is what decides, not the date.
        /// </summary>
        [Fact]
        public async Task ExpireOneTimePremiumAsync_LeavesARecurringSubscriptionEvenWithAPastExpiry()
        {
            var userId = await SeedUserAsync(
                "subscriber", isPremium: true, subscriptionType: "subscription",
                expiresAt: DateTime.UtcNow.AddDays(-5), payPalSubscriptionId: "SUB-1");

            await using var act = NewContext();
            await NewSut(act).ExpireOneTimePremiumAsync();

            Assert.True((await ReadUserAsync(userId)).IsPremium);
        }

        [Fact]
        public async Task ExpireOneTimePremiumAsync_IgnoresUsersWhoWereNeverPremium()
        {
            var userId = await SeedUserAsync(
                "free", isPremium: false, subscriptionType: null, expiresAt: null,
                maxGroups: PlanLimits.FreeMaxGroups, payPalOrderId: null);

            await using var act = NewContext();
            await NewSut(act).ExpireOneTimePremiumAsync();

            var user = await ReadUserAsync(userId);

            Assert.False(user.IsPremium);
            Assert.Null(user.UpdatedAt);
        }

        [Fact]
        public async Task ExpireOneTimePremiumAsync_ExpiresEveryEligibleUserInOneSweep()
        {
            var first = await SeedUserAsync("expired-one", true, "onetime", DateTime.UtcNow.AddDays(-1));
            var second = await SeedUserAsync("expired-two", true, "onetime", DateTime.UtcNow.AddDays(-9));
            var untouched = await SeedUserAsync("still-paid", true, "onetime", DateTime.UtcNow.AddDays(3));

            await using var act = NewContext();
            await NewSut(act).ExpireOneTimePremiumAsync();

            Assert.False((await ReadUserAsync(first)).IsPremium);
            Assert.False((await ReadUserAsync(second)).IsPremium);
            Assert.True((await ReadUserAsync(untouched)).IsPremium);
        }

        /// <summary>
        /// Hangfire retries this job on failure, so a second run over the same data has to be a
        /// no op rather than a second round of writes.
        /// </summary>
        [Fact]
        public async Task ExpireOneTimePremiumAsync_IsSafeToRunTwice()
        {
            var userId = await SeedUserAsync(
                "expired", isPremium: true, subscriptionType: "onetime",
                expiresAt: DateTime.UtcNow.AddDays(-1));

            await using (var first = NewContext())
                await NewSut(first).ExpireOneTimePremiumAsync();

            var afterFirstRun = (await ReadUserAsync(userId)).UpdatedAt;

            await using (var second = NewContext())
                await NewSut(second).ExpireOneTimePremiumAsync();

            var user = await ReadUserAsync(userId);

            Assert.False(user.IsPremium);
            Assert.Equal(afterFirstRun, user.UpdatedAt);
        }

        [Fact]
        public async Task ExpireOneTimePremiumAsync_DoesNothingWhenNobodyIsEligible()
        {
            await SeedUserAsync("free", isPremium: false, subscriptionType: null, expiresAt: null);

            await using var act = NewContext();
            var thrown = await Record.ExceptionAsync(() => NewSut(act).ExpireOneTimePremiumAsync());

            Assert.Null(thrown);
        }
    }
}
