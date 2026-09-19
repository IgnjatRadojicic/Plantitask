
namespace Plantitask.Core.DTO.Users
{
    /// <summary>
    /// Who the user is. Identity only.
    ///
    /// The premium fields left on 2026-08-17. They are subscription state rather than identity,
    /// they already had two endpoints of their own, and carrying a third copy here is what
    /// forced this payload to be assembled from two sources. Clients read premium from
    /// GET /api/user/profile/entitlements.
    /// </summary>
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
    }
}
