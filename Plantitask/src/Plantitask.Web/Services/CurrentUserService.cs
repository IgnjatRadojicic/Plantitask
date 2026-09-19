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

        // The profile carries identity only since 2026-08-17. Premium state comes from the
        // entitlements endpoint, and the two are fetched together so the header waits for one
        // round trip rather than two.
        try
        {
            var profileTask = _profileService.GetProfileAsync();
            var planTask = _profileService.GetEntitlementsAsync();
            await Task.WhenAll(profileTask, planTask);

            var profile = profileTask.Result;
            if (profile.Success && profile.Data is not null)
                info.ProfilePicturePath = profile.Data.ProfilePicturePath;

            var plan = planTask.Result;
            if (plan.Success && plan.Data is not null)
            {
                info.IsPremium = plan.Data.IsPremium;
                info.SubscriptionType = plan.Data.SubscriptionType;
                info.PremiumExpiresAt = plan.Data.ExpiresAt;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CurrentUserService] profile or entitlements load failed: {ex.Message}");
        }

        _cached = info;
        return _cached;
    }

    public void ClearCache() => _cached = null;
}