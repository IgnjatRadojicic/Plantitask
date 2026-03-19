using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

namespace Plantitask.Web.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly AuthenticationStateProvider _authStateProvider;

    public CurrentUserService(AuthenticationStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
    }

    public async Task<UserInfo?> GetCurrentUserAsync()
    {
        var state = await _authStateProvider.GetAuthenticationStateAsync();
        var user = state.User;

        if (user.Identity?.IsAuthenticated != true)
            return null;

        var c = user.Claims;

        return new UserInfo
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
    }
}