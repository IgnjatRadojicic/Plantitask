using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Plans;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Plans;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// Resolves plans and grants. See docs/rewrite/services/entitlements.md for the model.
    ///
    /// The shape to keep in mind: a grant says which plan version a user holds and until when,
    /// and the version says what that plan grants. Neither is ever recomputed into a column on
    /// User, so there is no writer to get wrong and no window where a stale copy is still being
    /// enforced.
    /// </summary>
    public class EntitlementService : IEntitlementService
    {
        private const string CurrentVersionCacheKeyPrefix = "plan-current-version-";
        private static readonly TimeSpan CatalogCacheTtl = TimeSpan.FromSeconds(60);

        private readonly IApplicationDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly ILogger<EntitlementService> _logger;

        public EntitlementService(
            IApplicationDbContext context,
            IMemoryCache cache,
            ILogger<EntitlementService> logger)
        {
            _context = context;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// The active grant wins by tier first, so a 30 day pass bought during a live
        /// subscription can never downgrade anybody. Open ended beats dated, then latest expiry.
        /// No active grant is the free plan, which is why free users need no row.
        /// </summary>
        public async Task<Result<UserEntitlements>> GetEntitlementsAsync(Guid userId)
        {
            var now = DateTime.UtcNow;

            var grant = await _context.UserPlanGrants
                .Where(g => g.UserId == userId
                    && g.StartsAt <= now
                    && (g.EndsAt == null || g.EndsAt > now))
                .OrderByDescending(g => g.PlanVersion.PlanId)
                .ThenByDescending(g => g.EndsAt == null)
                .ThenByDescending(g => g.EndsAt)
                .Select(g => new ResolvedGrant(
                    (PlanTier)g.PlanVersion.PlanId,
                    g.PlanVersion.Plan.Name,
                    g.PlanVersion.Plan.DisplayName,
                    g.PlanVersion.MaxGroups,
                    g.PlanVersion.MaxStorageBytes,
                    g.Source,
                    g.StartsAt,
                    g.EndsAt))
                .FirstOrDefaultAsync();

            if (grant is not null)
            {
                return new UserEntitlements
                {
                    Tier = grant.Tier,
                    PlanKey = grant.PlanKey,
                    PlanDisplayName = grant.PlanDisplayName,
                    MaxGroups = grant.MaxGroups,
                    MaxStorageBytes = grant.MaxStorageBytes,
                    Source = grant.Source,
                    StartsAt = grant.StartsAt,
                    EndsAt = grant.EndsAt
                };
            }

            // No grant. Confirm the user exists before claiming they are on the free plan,
            // otherwise a deleted id would silently resolve to free limits.
            if (!await _context.Users.AnyAsync(u => u.Id == userId))
                return Error.NotFound("User not found");

            var free = await GetCurrentVersionAsync(PlanTier.Free);

            return new UserEntitlements
            {
                Tier = PlanTier.Free,
                PlanKey = free.PlanKey,
                PlanDisplayName = free.PlanDisplayName,
                MaxGroups = free.MaxGroups,
                MaxStorageBytes = free.MaxStorageBytes
            };
        }

        public async Task<Result<EntitlementsDto>> GetUsageAsync(Guid userId)
        {
            var result = await GetEntitlementsAsync(userId);
            if (result.IsFailure)
                return result.Error!;

            var e = result.Value!;

            return new EntitlementsDto
            {
                PlanKey = e.PlanKey,
                PlanDisplayName = e.PlanDisplayName,
                IsPremium = e.IsPremium,
                SubscriptionType = e.SubscriptionType,
                StartedAt = e.StartsAt,
                ExpiresAt = e.EndsAt,
                MaxGroups = e.MaxGroups,
                GroupsUsed = await GetGroupCountAsync(userId),
                MaxStorageBytes = e.MaxStorageBytes,
                StorageUsedBytes = await GetStorageUsedBytesAsync(userId)
            };
        }

        /// <summary>
        /// Counted rather than stored. A cached column would need decrementing in
        /// DeleteAttachmentAsync, whose storage delete failure is swallowed, so the column and
        /// the disk would diverge on every failed delete. The soft delete filter already makes
        /// this sum track what is really on disk.
        ///
        /// The long? cast is not optional. SQL SUM over zero rows is NULL and mapping that onto
        /// a non-nullable long throws for the first user who has never uploaded.
        /// </summary>
        public async Task<long> GetStorageUsedBytesAsync(Guid userId)
        {
            return await _context.TaskAttachments
                .Where(a => a.CreatedBy == userId)
                .SumAsync(a => (long?)a.FileSize) ?? 0L;
        }

        public async Task<int> GetGroupCountAsync(Guid userId)
        {
            return await _context.GroupMembers.CountAsync(gm => gm.UserId == userId);
        }

        public async Task<Result> StageGrantAsync(
            Guid userId, PlanTier tier, DateTime? endsAt, string source, string? payPalRef, Guid grantedBy)
        {
            var version = await GetCurrentVersionAsync(tier);

            _context.UserPlanGrants.Add(new UserPlanGrant
            {
                UserId = userId,
                PlanVersionId = version.VersionId,
                StartsAt = DateTime.UtcNow,
                EndsAt = endsAt,
                Source = source,
                PayPalRef = payPalRef,
                GrantedBy = grantedBy
            });

            _logger.LogInformation(
                "Staged {Tier} grant for user {UserId} from {Source} ending {EndsAt}",
                tier, userId, source, endsAt);

            return Result.Success();
        }

        public async Task<UserPlanGrant?> FindOpenGrantByPayPalRefAsync(string payPalRef)
        {
            return await _context.UserPlanGrants
                .FirstOrDefaultAsync(g => g.PayPalRef == payPalRef && g.EndsAt == null);
        }

        public async Task<UserPlanGrant?> FindActiveGrantAsync(Guid userId, string source)
        {
            var now = DateTime.UtcNow;

            return await _context.UserPlanGrants
                .Where(g => g.UserId == userId
                    && g.Source == source
                    && g.StartsAt <= now
                    && (g.EndsAt == null || g.EndsAt > now))
                .OrderByDescending(g => g.EndsAt == null)
                .ThenByDescending(g => g.EndsAt)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> AnyGrantForPayPalRefAsync(string payPalRef)
        {
            return await _context.UserPlanGrants.AnyAsync(g => g.PayPalRef == payPalRef);
        }

        public void EndGrant(UserPlanGrant grant, bool cancelled, Guid? endedBy)
        {
            var now = DateTime.UtcNow;

            grant.EndsAt = now;
            grant.EndedBy = endedBy;

            if (cancelled)
                grant.CancelledAt = now;
        }

        public void InvalidateCatalogCache()
        {
            foreach (PlanTier tier in Enum.GetValues<PlanTier>())
                _cache.Remove(CurrentVersionCacheKeyPrefix + (int)tier);
        }

        /// <summary>
        /// The published version in force right now. Drafts and future-dated versions are
        /// ignored. Cached briefly because this sits on the group-create and upload paths and
        /// the rows change roughly never; InvalidateCatalogCache makes an admin edit immediate.
        /// </summary>
        private async Task<CurrentVersion> GetCurrentVersionAsync(PlanTier tier)
        {
            var key = CurrentVersionCacheKeyPrefix + (int)tier;

            if (_cache.TryGetValue<CurrentVersion>(key, out var cached) && cached is not null)
                return cached;

            var now = DateTime.UtcNow;

            var version = await _context.PlanVersions
                .Where(v => v.PlanId == (int)tier
                    && v.PublishedAt != null
                    && v.EffectiveFrom <= now)
                .OrderByDescending(v => v.EffectiveFrom)
                .ThenByDescending(v => v.Version)
                .Select(v => new CurrentVersion(
                    v.Id, v.Plan.Name, v.Plan.DisplayName, v.MaxGroups, v.MaxStorageBytes))
                .FirstOrDefaultAsync();

            if (version is null)
            {
                // A seed or migration fault, not a user error. Fail loudly rather than hand out
                // silent zeroes that would lock every user out of creating anything.
                throw new InvalidOperationException(
                    $"No published plan version in force for plan '{tier}'. Check the plan_versions seed.");
            }

            _cache.Set(key, version, CatalogCacheTtl);
            return version;
        }

        private sealed record CurrentVersion(
            Guid VersionId, string PlanKey, string PlanDisplayName, int MaxGroups, long MaxStorageBytes);

        private sealed record ResolvedGrant(
            PlanTier Tier,
            string PlanKey,
            string PlanDisplayName,
            int MaxGroups,
            long MaxStorageBytes,
            string Source,
            DateTime StartsAt,
            DateTime? EndsAt);
    }
}
