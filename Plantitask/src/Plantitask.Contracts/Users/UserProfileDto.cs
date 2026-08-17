
namespace Plantitask.Core.DTO.Users
{
    public class UserProfileDto
    {
        public Guid Id { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        // Storage key, not a URL. The client composes the address (Web: FileUrls.ToUrl).
        public string? ProfilePicturePath { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsPremium { get; set; }
        public string? SubscriptionType { get; set; }
        public DateTime? PremiumExpiresAt { get; set; }
        public DateTime? PremiumStartedAt { get; set; }
    }
}
