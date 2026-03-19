namespace Plantitask.Web.Models;

public class UserInfo
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? ProfilePictureUrl { get; set; }

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


    public static UserInfo FromAuthResponse(AuthResponse auth) => new()
    {
        Id = auth.UserId,
        UserName = auth.UserName,
        Email = auth.Email
    };
}