using Plantitask.Core.Enums;

namespace Plantitask.Core.Plans
{
    /// <summary>
    /// The resolved answer to "what may this user do right now", derived from the active grant
    /// and its pinned plan version. Never stored. Every enforcement site takes its limit from
    /// here so there is exactly one place the rule lives.
    /// </summary>
    public sealed class UserEntitlements
    {
        public required PlanTier Tier { get; init; }
        public required string PlanKey { get; init; }
        public required string PlanDisplayName { get; init; }

        public required int MaxGroups { get; init; }
        public required long MaxStorageBytes { get; init; }

        /// <summary>Null when the user is on the free plan, which has no grant row.</summary>
        public string? Source { get; init; }
        public DateTime? StartsAt { get; init; }
        public DateTime? EndsAt { get; init; }

        public bool IsPremium => Tier != PlanTier.Free;

        /// <summary>
        /// The value the client has always received in PremiumStatusDto.SubscriptionType and
        /// UserProfileDto.SubscriptionType. AccountSettings.razor branches on "recurring" to
        /// decide whether to offer a cancel button, so these strings are a wire contract and do
        /// not follow GrantSource when it is renamed.
        /// </summary>
        public string? SubscriptionType => Source switch
        {
            GrantSource.PayPalSubscription => "recurring",
            GrantSource.PayPalOneTime => "onetime",
            GrantSource.AdminGrant => "admin",
            _ => null
        };
    }
}
