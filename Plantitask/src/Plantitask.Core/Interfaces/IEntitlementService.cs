using Plantitask.Core.Common;
using Plantitask.Core.DTO.Plans;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Plans;

namespace Plantitask.Core.Interfaces
{
    /// <summary>
    /// The single seam between "what a user paid for" and "what a user may do". Every enforcement
    /// site asks this instead of reading a column, which is why no stored entitlement can drift
    /// out of step with the payment state that produced it.
    ///
    /// The grant lifecycle methods stage changes only and never call SaveChangesAsync. The
    /// webhook pipeline commits the processed-event marker and the grant change together in one
    /// save, so ownership of the commit has to stay with the caller.
    /// </summary>
    public interface IEntitlementService
    {
        /// <summary>Resolved limits for a user. Falls back to the free plan when no grant is active.</summary>
        Task<Result<UserEntitlements>> GetEntitlementsAsync(Guid userId);

        /// <summary>Limits plus current usage, for the client.</summary>
        Task<Result<EntitlementsDto>> GetUsageAsync(Guid userId);

        /// <summary>Bytes this user has uploaded across every group. Counted, never stored.</summary>
        Task<long> GetStorageUsedBytesAsync(Guid userId);

        /// <summary>Groups this user belongs to, owned or joined.</summary>
        Task<int> GetGroupCountAsync(Guid userId);

        /// <summary>
        /// Stages a grant pinned to the plan's current published version. Does not save.
        /// </summary>
        Task<Result> StageGrantAsync(
            Guid userId, PlanTier tier, DateTime? endsAt, string source, string? payPalRef, Guid grantedBy);

        /// <summary>The open grant carrying this PayPal reference, if any.</summary>
        Task<UserPlanGrant?> FindOpenGrantByPayPalRefAsync(string payPalRef);

        /// <summary>The user's active grant from a given source, if any.</summary>
        Task<UserPlanGrant?> FindActiveGrantAsync(Guid userId, string source);

        /// <summary>
        /// Whether this PayPal reference was ever granted, open or closed. The idempotency check
        /// for one-time orders, which must never be granted twice even after they expire.
        /// </summary>
        Task<bool> AnyGrantForPayPalRefAsync(string payPalRef);

        /// <summary>
        /// Ends a grant now. Does not save. Pass null for endedBy when a webhook or a natural
        /// expiry ended it rather than a person.
        /// </summary>
        void EndGrant(UserPlanGrant grant, bool cancelled, Guid? endedBy);

        /// <summary>
        /// Drops the cached current-version lookup. The admin publish endpoint calls this so an
        /// edit applies immediately instead of within the cache window.
        /// </summary>
        void InvalidateCatalogCache();
    }
}
