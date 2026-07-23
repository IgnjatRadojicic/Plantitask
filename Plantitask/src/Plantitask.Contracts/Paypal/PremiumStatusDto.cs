
namespace Plantitask.Core.DTO.Paypal
{
    public class PremiumStatusDto
    {
        public bool IsPremium { get; set; }
        public string? SubscriptionType { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public bool CanUseDarkMode { get; set; }
        public int MaxGroups { get; set; }
    }
}
