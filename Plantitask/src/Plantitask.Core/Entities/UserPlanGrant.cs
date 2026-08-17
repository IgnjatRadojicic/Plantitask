using Plantitask.Core.Common;

namespace Plantitask.Core.Entities
{
    /// <summary>
    /// One period during which a user holds a plan. This is the whole answer to "is this user
    /// premium", replacing IsPremium, PremiumExpiresAt, PremiumStartedAt, SubscriptionType,
    /// PayPalSubscriptionId and PayPalOrderId on User.
    ///
    /// There is no boolean to flip and therefore no window in which it has not been flipped yet.
    /// Expiry is EndsAt passing, which needs no job and cannot drift.
    ///
    /// Free users have no row. Absence of an active grant is the free plan.
    ///
    /// ImmutableEntity rather than BaseEntity, so there is no IsDeleted and no way to delete a
    /// grant at all. This is a record that money changed hands and it is superseded, never
    /// erased: ending it is what EndsAt is for, and that leaves the history readable. It also
    /// removes a failure mode, since a soft delete filter could silently drop a paying user to
    /// the free plan with nothing raising an error.
    /// </summary>
    public class UserPlanGrant : ImmutableEntity
    {
        public Guid UserId { get; set; }

        /// <summary>The version pin. Fixes what this grant is worth for its whole life.</summary>
        public Guid PlanVersionId { get; set; }

        public DateTime StartsAt { get; set; }

        /// <summary>Null is open ended, meaning PayPal is still charging.</summary>
        public DateTime? EndsAt { get; set; }

        /// <summary>See GrantSource.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>PayPal subscription id or order id. The idempotency key for webhooks.</summary>
        public string? PayPalRef { get; set; }

        /// <summary>
        /// Set when a subscription was ended early. EndsAt is what enforcement reads; this is
        /// only the record that the ending was a cancellation rather than a natural expiry.
        /// </summary>
        public DateTime? CancelledAt { get; set; }

        /// <summary>Who granted it. The buyer for a PayPal purchase, an admin for a comp.</summary>
        public Guid GrantedBy { get; set; }

        /// <summary>
        /// Who ended it. Null when it expired on its own or a PayPal webhook ended it, since
        /// neither has a person behind it.
        /// </summary>
        public Guid? EndedBy { get; set; }

        public User User { get; set; } = null!;
        public PlanVersion PlanVersion { get; set; } = null!;
    }
}
