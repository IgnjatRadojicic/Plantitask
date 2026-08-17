
namespace Plantitask.Core.DTO.Paypal
{
    /// <summary>
    /// Subscription state only. Limits live in EntitlementsDto: a payload describing what someone
    /// bought should not also be the place the app learns what they may do.
    /// </summary>
    public class PremiumStatusDto
    {
        public bool IsPremium { get; set; }
        public string? SubscriptionType { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? StartedAt { get; set; }
    }
}
