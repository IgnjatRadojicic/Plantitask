namespace Plantitask.Web.Models;

// === User Profile Models ===

public class UserProfileDto
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public bool IsPremium { get; set; }
    public string? SubscriptionType { get; set; }
    public DateTime? PremiumExpiresAt { get; set; }
    public DateTime? PremiumStartedAt { get; set; }
    public int MaxGroups { get; set; } = 5;

    public string DisplayName
    {
        get
        {
            var full = $"{FirstName} {LastName}".Trim();
            return string.IsNullOrEmpty(full) ? UserName : full;
        }
    }

    public string Initials
    {
        get
        {
            if (!string.IsNullOrEmpty(FirstName) && !string.IsNullOrEmpty(LastName))
                return $"{FirstName[0]}{LastName[0]}".ToUpper();
            if (!string.IsNullOrEmpty(UserName) && UserName.Length >= 2)
                return UserName[..2].ToUpper();
            return "?";
        }
    }
}


public class PremiumStatusDto
{
    public bool IsPremium { get; set; }
    public string? SubscriptionType { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public bool CanUseDarkMode { get; set; }
    public int MaxGroups { get; set; }
}

public class UpdateUserProfileDto
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? UserName { get; set; }
}

public class ChangePasswordDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

public class ProfilePictureResponse
{
    public string Url { get; set; } = string.Empty;
}