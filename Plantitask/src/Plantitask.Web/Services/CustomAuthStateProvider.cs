using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace Plantitask.Web.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {

        private readonly ILocalStorageService _localStorage;

        private static readonly AuthenticationState AnonymousState =
            new(new ClaimsPrincipal(new ClaimsIdentity()));

        public CustomAuthStateProvider(ILocalStorageService localStorage)
        {
            _localStorage = localStorage;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                var token = await _localStorage.GetItemAsStringAsync("authToken");

                if (string.IsNullOrWhiteSpace(token))
                    return AnonymousState;

                var claims = ParseClaimsFromJwt(token);

                var expClaim = claims.FirstOrDefault(c =>
                c.Type == "exp" || c.Type == JwtRegisteredClaimNames.Exp);

                if (expClaim != null &&
                    long.TryParse(expClaim.Value, out var expSeconds))
                {
                    var expiry = DateTimeOffset.FromUnixTimeSeconds(expSeconds);
                    if (expiry <= DateTimeOffset.UtcNow)
                    {
                        await _localStorage.RemoveItemAsync("authToken");
                        await _localStorage.RemoveItemAsync("refreshToken");
                        return AnonymousState;
                    }
                }

                var identity = new ClaimsIdentity(claims, "Bearer");
                return new AuthenticationState(new ClaimsPrincipal(identity));
            }
            catch
            {
                return AnonymousState;
            }
        }

        public void NotifyUserAuthentication(string token)
        {
            var claims = ParseClaimsFromJwt(token);
            var identity = new ClaimsIdentity(claims, "Bearer");
            var state = Task.FromResult(
                new AuthenticationState(new ClaimsPrincipal(identity)));

            NotifyAuthenticationStateChanged(state);
        }


        public void NotifyUserLogout()
        {
            NotifyAuthenticationStateChanged(
                Task.FromResult(AnonymousState));
        }

        private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwt);
            return token.Claims;
        }
    }

}
