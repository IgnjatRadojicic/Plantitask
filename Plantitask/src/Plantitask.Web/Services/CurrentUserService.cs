using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

using Plantitask.Core.DTO.Users;
namespace Plantitask.Web.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly IUserProfileService _profileService;
    private UserInfo? _cached;

    public CurrentUserService(
        AuthenticationStateProvider authStateProvider,
        IUserProfileService profileService)
    {
        _authStateProvider = authStateProvider;
        _profileService = profileService;
    }

    public async Task<UserInfo?> GetCurrentUserAsync()
    {
        if (_cached is not null) return _cached;

        var state = await _authStateProvider.GetAuthenticationStateAsync();
        var user = state.User;

        if (user.Identity?.IsAuthenticated != true)
            return null;

        var c = user.Claims;

        var info = new UserInfo
        {
            Id = Guid.TryParse(
                c.FirstOrDefault(x => x.Type is "sub" or "nameid")?.Value,
                out var id) ? id : Guid.Empty,
            UserName = c.FirstOrDefault(x => x.Type == "unique_name")?.Value
                     ?? c.FirstOrDefault(x => x.Type == ClaimTypes.Name)?.Value
                     ?? "User",
            Email = c.FirstOrDefault(x => x.Type == "email")?.Value
                     ?? c.FirstOrDefault(x => x.Type == ClaimTypes.Email)?.Value
                     ?? "",
            FirstName = c.FirstOrDefault(x => x.Type == "given_name")?.Value
                     ?? c.FirstOrDefault(x => x.Type == ClaimTypes.GivenName)?.Value,
            LastName = c.FirstOrDefault(x => x.Type == "family_name")?.Value
                     ?? c.FirstOrDefault(x => x.Type == ClaimTypes.Surname)?.Value,
        };

        try
        {
            var result = await _profileService.GetProfileAsync();
            if (result.Success && result.Data is not null)
            {
                info.ProfilePictureUrl = result.Data.ProfilePictureUrl;
                info.IsPremium = result.Data.IsPremium;
                info.SubscriptionType = result.Data.SubscriptionType;
                info.PremiumExpiresAt = result.Data.PremiumExpiresAt;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CurrentUserService] GetProfileAsync failed: {ex.Message}");
        }

        _cached = info;
        return _cached;
    }

    public void ClearCache() => _cached = null;
}