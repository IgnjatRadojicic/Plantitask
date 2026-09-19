using Microsoft.EntityFrameworkCore;
using Npgsql;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class EntitlementServiceTests : DbTestBase
    {
        private const long Mb = 1024 * 1024;

        public EntitlementServiceTests(PostgresFixture fixture) : base(fixture)
        {
        }

        private static EntitlementService NewSut(IApplicationDbContext context) =>
            TestServices.Entitlements(context);

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
            db.Tasks.Add(TestData.Task(GroupId, LeadId, id: TaskId));
            await db.SaveChangesAsync();
        }

        private async Task<Guid> SeedGrantAsync(
            Guid userId,
            string source,
            DateTime? endsAt,
            DateTime? startsAt = null,
            string? payPalRef = null,
            PlanTier tier = PlanTier.Premium)
        {
            await using var db = NewContext();
            return await db.SeedGrantAsync(userId, source, payPalRef, endsAt, startsAt, tier);
        }

        private async Task<Guid> PublishVersionAsync(
            PlanTier tier, int version, int maxGroups, long maxStorageBytes,
            DateTime? effectiveFrom = null, bool published = true)
        {
            await using var db = NewContext();

            var planVersion = new PlanVersion
            {
                PlanId = (int)tier,
                Version = version,
                EffectiveFrom = effectiveFrom ?? DateTime.UtcNow.AddMinutes(-1),
                PublishedAt = published ? DateTime.UtcNow : null,
                MaxGroups = maxGroups,
                MaxStorageBytes = maxStorageBytes
            };

            db.PlanVersions.Add(planVersion);
            await db.SaveChangesAsync();

            return planVersion.Id;
        }

        private async Task<Guid> VersionIdAsync(PlanTier tier, int version)
        {
            await using var db = NewContext();
            return await db.PlanVersions
                .Where(v => v.PlanId == (int)tier && v.Version == version)
                .Select(v => v.Id)
                .SingleAsync();
        }

        private async Task SeedAttachmentAsync(Guid uploader, long fileSize, bool deleted = false)
        {
            await using var db = NewContext();

            db.TaskAttachments.Add(new TaskAttachment
            {
                TaskId = TaskId,
                FileName = "seeded.png",
                FilePath = $"attachments/{Guid.NewGuid()}.png",
                ContentType = "image/png",
                FileSize = fileSize,
                CreatedBy = uploader,
                IsDeleted = deleted,
                DeletedAt = deleted ? DateTime.UtcNow : null
            });

            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task GetEntitlementsAsync_ResolvesTheFreePlanForAUserWithNoGrant()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetEntitlementsAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            var entitlements = result.Value!;
            Assert.Equal(PlanTier.Free, entitlements.Tier);
            Assert.Equal("free", entitlements.PlanKey);
            Assert.Equal(5, entitlements.MaxGroups);
            Assert.Equal(50 * Mb, entitlements.MaxStorageBytes);
            Assert.False(entitlements.IsPremium);
            Assert.Null(entitlements.Source);
            Assert.Null(entitlements.SubscriptionType);
        }

        /// <summary>
        /// Free is the answer for anyone without a grant, so an id that matches nobody has to be
        /// caught before that fallback. Otherwise a deleted user would quietly resolve to free
        /// limits and the caller would never learn the id was wrong.
        /// </summary>
        [Fact]
        public async Task GetEntitlementsAsync_ReturnsNotFoundForAUserWhoDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetEntitlementsAsync(Guid.NewGuid());

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task GetEntitlementsAsync_ResolvesPremiumFromARunningSubscription()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, endsAt: null);

            await using var act = NewContext();
            var entitlements = (await NewSut(act).GetEntitlementsAsync(MemberId)).Value!;

            Assert.Equal(PlanTier.Premium, entitlements.Tier);
            Assert.Equal(10, entitlements.MaxGroups);
            Assert.Equal(500 * Mb, entitlements.MaxStorageBytes);
            Assert.Equal("recurring", entitlements.SubscriptionType);
            Assert.Null(entitlements.EndsAt);
        }

        /// <summary>
        /// Expiry is nothing more than the clock passing EndsAt, which is why no job has to run
        /// for premium to end. The cases pin both edges of the window and the open ended case a
        /// live subscription uses.
        /// </summary>
        [Theory]
        [InlineData(-60, 60, true)]
        [InlineData(-60, null, true)]
        [InlineData(-60, -1, false)]
        [InlineData(1, 60, false)]
        public async Task GetEntitlementsAsync_AGrantAppliesOnlyBetweenItsStartAndEnd(
            int startsInMinutes, int? endsInMinutes, bool expectPremium)
        {
            await SeedAsync();
            var now = DateTime.UtcNow;
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime,
                endsAt: endsInMinutes.HasValue ? now.AddMinutes(endsInMinutes.Value) : null,
                startsAt: now.AddMinutes(startsInMinutes));

            await using var act = NewContext();
            var entitlements = (await NewSut(act).GetEntitlementsAsync(MemberId)).Value!;

            Assert.Equal(expectPremium, entitlements.IsPremium);
        }

        [Fact]
        public async Task GetEntitlementsAsync_AnOpenEndedSubscriptionWinsOverAPassBoughtDuringIt()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, endsAt: null);
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime, endsAt: DateTime.UtcNow.AddDays(10));

            await using var act = NewContext();
            var entitlements = (await NewSut(act).GetEntitlementsAsync(MemberId)).Value!;

            Assert.Equal(GrantSource.PayPalSubscription, entitlements.Source);
            Assert.Null(entitlements.EndsAt);
        }

        [Fact]
        public async Task GetEntitlementsAsync_BetweenTwoPassesTheLaterExpiryWins()
        {
            await SeedAsync();
            var later = DateTime.UtcNow.AddDays(20);
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime, endsAt: DateTime.UtcNow.AddDays(5));
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime, endsAt: later);

            await using var act = NewContext();
            var entitlements = (await NewSut(act).GetEntitlementsAsync(MemberId)).Value!;

            Assert.Equal(later, entitlements.EndsAt!.Value, TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// Tier is sorted before anything else. Sorted by open ended first, the never ending free
        /// grant here would win and the user would lose the premium they paid for while it was
        /// still running.
        /// </summary>
        [Fact]
        public async Task GetEntitlementsAsync_AHigherTierWinsEvenOverAnOpenEndedLowerOne()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.AdminGrant, endsAt: null, tier: PlanTier.Free);
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime, endsAt: DateTime.UtcNow.AddDays(10));

            await using var act = NewContext();
            var entitlements = (await NewSut(act).GetEntitlementsAsync(MemberId)).Value!;

            Assert.Equal(PlanTier.Premium, entitlements.Tier);
            Assert.Equal(GrantSource.PayPalOneTime, entitlements.Source);
        }

        /// <summary>
        /// Grandfathering. Publishing a new version must not reprice anybody who already bought,
        /// so a grant keeps the limits of the version it was pinned to when it was sold.
        /// </summary>
        [Fact]
        public async Task GetEntitlementsAsync_AnExistingGrantKeepsTheLimitsOfTheVersionItWasSold()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, endsAt: null);
            await PublishVersionAsync(PlanTier.Premium, version: 2, maxGroups: 20, maxStorageBytes: 1000 * Mb);

            await using var act = NewContext();
            var entitlements = (await NewSut(act).GetEntitlementsAsync(MemberId)).Value!;

            Assert.Equal(10, entitlements.MaxGroups);
            Assert.Equal(500 * Mb, entitlements.MaxStorageBytes);
        }

        [Fact]
        public async Task StageGrantAsync_PinsANewGrantToTheCurrentPublishedVersion()
        {
            await SeedAsync();
            var v2 = await PublishVersionAsync(PlanTier.Premium, version: 2, maxGroups: 20, maxStorageBytes: 1000 * Mb);

            await using (var act = NewContext())
            {
                await NewSut(act).StageGrantAsync(
                    MemberId, PlanTier.Premium, endsAt: null, GrantSource.PayPalSubscription, "I-SUB-1", MemberId);
                await act.SaveChangesAsync();
            }

            await using var assert = NewContext();
            var grant = await assert.UserPlanGrants.SingleAsync(g => g.UserId == MemberId);
            Assert.Equal(v2, grant.PlanVersionId);

            var entitlements = (await NewSut(assert).GetEntitlementsAsync(MemberId)).Value!;
            Assert.Equal(20, entitlements.MaxGroups);
        }

        /// <summary>
        /// A draft has no PublishedAt and a scheduled version has an EffectiveFrom still ahead.
        /// Neither may be sold yet, so a grant staged today still pins version 1.
        /// </summary>
        [Fact]
        public async Task StageGrantAsync_IgnoresDraftsAndVersionsNotYetInEffect()
        {
            await SeedAsync();
            await PublishVersionAsync(PlanTier.Premium, version: 2, maxGroups: 99, maxStorageBytes: 1000 * Mb,
                published: false);
            await PublishVersionAsync(PlanTier.Premium, version: 3, maxGroups: 77, maxStorageBytes: 1000 * Mb,
                effectiveFrom: DateTime.UtcNow.AddDays(1));

            await using (var act = NewContext())
            {
                await NewSut(act).StageGrantAsync(
                    MemberId, PlanTier.Premium, endsAt: null, GrantSource.PayPalSubscription, "I-SUB-1", MemberId);
                await act.SaveChangesAsync();
            }

            await using var assert = NewContext();
            var grant = await assert.UserPlanGrants.SingleAsync(g => g.UserId == MemberId);
            Assert.Equal(await VersionIdAsync(PlanTier.Premium, 1), grant.PlanVersionId);
        }

        /// <summary>
        /// The webhook pipeline saves the processed event marker and the grant in one commit, so
        /// the stage call must leave the save to its caller. A stage that saved on its own would
        /// let a grant land while its marker failed and the redelivery would then grant again.
        /// </summary>
        [Fact]
        public async Task StageGrantAsync_LeavesTheSaveToTheCaller()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).StageGrantAsync(
                MemberId, PlanTier.Premium, endsAt: null, GrantSource.PayPalSubscription, "I-SUB-1", MemberId);

            await using (var before = NewContext())
                Assert.Empty(await before.UserPlanGrants.ToListAsync());

            await act.SaveChangesAsync();

            await using var after = NewContext();
            Assert.Single(await after.UserPlanGrants.ToListAsync());
        }

        /// <summary>
        /// A missing catalogue is a seed or migration fault. Handing out zero limits instead
        /// would lock every user out of creating anything with nothing in the logs saying why.
        /// </summary>
        [Fact]
        public async Task GetEntitlementsAsync_FailsLoudlyWhenNoVersionOfThePlanIsInForce()
        {
            await SeedAsync();

            await using (var db = NewContext())
                await db.PlanVersions.Where(v => v.PlanId == (int)PlanTier.Free).ExecuteDeleteAsync();

            await using var act = NewContext();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => NewSut(act).GetEntitlementsAsync(MemberId));
        }

        [Fact]
        public async Task InvalidateCatalogCache_MakesANewlyPublishedVersionApplyImmediately()
        {
            await SeedAsync();

            await using var act = NewContext();
            var sut = NewSut(act);

            Assert.Equal(5, (await sut.GetEntitlementsAsync(MemberId)).Value!.MaxGroups);

            await PublishVersionAsync(PlanTier.Free, version: 2, maxGroups: 3, maxStorageBytes: 20 * Mb);

            Assert.Equal(5, (await sut.GetEntitlementsAsync(MemberId)).Value!.MaxGroups);

            sut.InvalidateCatalogCache();

            Assert.Equal(3, (await sut.GetEntitlementsAsync(MemberId)).Value!.MaxGroups);
        }

        /// <summary>
        /// EndsAt is what enforcement reads. CancelledAt and EndedBy are the record of how it
        /// ended, which is what separates a user pressing cancel from PayPal giving up on a card.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task EndGrant_StampsTheEndAndRecordsHowItEnded(bool cancelled)
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, endsAt: null, payPalRef: "I-SUB-1");

            await using (var act = NewContext())
            {
                var sut = NewSut(act);
                var grant = await sut.FindOpenGrantByPayPalRefAsync("I-SUB-1");
                sut.EndGrant(grant!, cancelled, endedBy: MemberId);
                await act.SaveChangesAsync();
            }

            await using var assert = NewContext();
            var ended = await assert.UserPlanGrants.SingleAsync();
            Assert.Equal(DateTime.UtcNow, ended.EndsAt!.Value, TimeSpan.FromMinutes(1));
            Assert.Equal(MemberId, ended.EndedBy);
            Assert.Equal(cancelled, ended.CancelledAt.HasValue);
        }

        /// <summary>
        /// The two lookups answer different questions on purpose. A subscription may be granted
        /// again once its grant closes, so its check looks at open grants only. A captured order
        /// must never be granted twice, so its check looks at every grant ever written.
        /// </summary>
        [Fact]
        public async Task PayPalRefLookups_OnlyTheAnyCheckStillSeesAClosedGrant()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime,
                endsAt: DateTime.UtcNow.AddDays(-1), startsAt: DateTime.UtcNow.AddDays(-31), payPalRef: "ORDER-1");

            await using var act = NewContext();
            var sut = NewSut(act);

            Assert.Null(await sut.FindOpenGrantByPayPalRefAsync("ORDER-1"));
            Assert.True(await sut.AnyGrantForPayPalRefAsync("ORDER-1"));
            Assert.False(await sut.AnyGrantForPayPalRefAsync("ORDER-2"));
        }

        [Fact]
        public async Task FindActiveGrantAsync_ReturnsOnlyTheGrantFromTheRequestedSource()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalOneTime, endsAt: DateTime.UtcNow.AddDays(10), payPalRef: "ORDER-1");
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, endsAt: null, payPalRef: "I-SUB-1");

            await using var act = NewContext();
            var grant = await NewSut(act).FindActiveGrantAsync(MemberId, GrantSource.PayPalSubscription);

            Assert.NotNull(grant);
            Assert.Equal("I-SUB-1", grant.PayPalRef);
        }

        /// <summary>
        /// Two deliveries of the same ACTIVATED event can both read "no grant" before either one
        /// inserts, so no application check can stop the second grant. The partial unique index
        /// can, and this proves it exists under the name the migration gave it.
        /// </summary>
        [Fact]
        public async Task TheDatabaseRefusesASecondOpenGrantForTheSamePayPalReference()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, endsAt: null, payPalRef: "I-SUB-1");

            await using var db = NewContext();
            var ex = await Assert.ThrowsAsync<DbUpdateException>(
                () => db.SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, "I-SUB-1"));

            var postgres = Assert.IsType<PostgresException>(ex.InnerException);
            Assert.Equal("IX_UserPlanGrants_PayPalRef_Open", postgres.ConstraintName);
        }

        [Fact]
        public async Task TheDatabaseAllowsANewOpenGrantOnceTheOldOneForThatReferenceClosed()
        {
            await SeedAsync();
            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription,
                endsAt: DateTime.UtcNow.AddDays(-5), startsAt: DateTime.UtcNow.AddDays(-40), payPalRef: "I-SUB-1");

            await SeedGrantAsync(MemberId, GrantSource.PayPalSubscription, endsAt: null, payPalRef: "I-SUB-1");

            await using var assert = NewContext();
            Assert.Equal(2, await assert.UserPlanGrants.CountAsync());
        }

        /// <summary>
        /// SQL SUM over no rows is NULL rather than 0. Without the nullable cast in the query
        /// this throws for exactly the user who has never uploaded, which is every new user.
        /// </summary>
        [Fact]
        public async Task GetStorageUsedBytesAsync_IsZeroForAUserWhoNeverUploaded()
        {
            await SeedAsync();

            await using var act = NewContext();
            Assert.Equal(0L, await NewSut(act).GetStorageUsedBytesAsync(MemberId));
        }

        [Fact]
        public async Task GetStorageUsedBytesAsync_CountsOnlyTheUsersOwnLiveUploads()
        {
            await SeedAsync();
            await SeedAttachmentAsync(MemberId, 100);
            await SeedAttachmentAsync(MemberId, 200);
            await SeedAttachmentAsync(MemberId, 400, deleted: true);
            await SeedAttachmentAsync(LeadId, 800);

            await using var act = NewContext();
            Assert.Equal(300L, await NewSut(act).GetStorageUsedBytesAsync(MemberId));
        }

        [Fact]
        public async Task GetUsageAsync_ReportsTheLimitsNextToWhatIsAlreadyUsed()
        {
            await SeedAsync();
            await SeedAttachmentAsync(LeadId, 1000);

            await using var act = NewContext();
            var result = await NewSut(act).GetUsageAsync(LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            var usage = result.Value!;
            Assert.Equal("free", usage.PlanKey);
            Assert.Equal("Free", usage.PlanDisplayName);
            Assert.False(usage.IsPremium);
            Assert.Equal(5, usage.MaxGroups);
            Assert.Equal(1, usage.GroupsUsed);
            Assert.Equal(50 * Mb, usage.MaxStorageBytes);
            Assert.Equal(1000L, usage.StorageUsedBytes);
        }
    }
}
