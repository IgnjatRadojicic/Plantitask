using Microsoft.AspNetCore.Components.Authorization;
using Plantitask.Web.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
namespace Plantitask.Web.Services
{
    public class CustomAuthStateProvider : AuthenticationStateProvider
    {

        private readonly ISessionService _session;

        private static readonly AuthenticationState AnonymousState =
            new(new ClaimsPrincipal(new ClaimsIdentity()));

        public CustomAuthStateProvider(ISessionService session)
        {
            _session = session;
            _session.OnTokensChanged += HandleTokensChanged;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            try
            {
                var token = await _session.GetAccessTokenAsync();

                if (string.IsNullOrWhiteSpace(token))
                    return AnonymousState;

                if (!IsExpiredOrExpiring(token))
                    return BuildState(token);

                var newToken = await _session.TryRefreshAsync();

                return newToken is not null ? BuildState(newToken) : AnonymousState;
            }
            catch
            {
                return AnonymousState;
            }
        }

        private void HandleTokensChanged(string? accessToken)
        {
            var state = accessToken is null ? AnonymousState : BuildState(accessToken);
            NotifyAuthenticationStateChanged(Task.FromResult(state));
        }
        private static AuthenticationState BuildState(string token) =>
            new(new ClaimsPrincipal(new ClaimsIdentity(ParseClaimsFromJwt(token), "Bearer")));

        private static bool IsExpiredOrExpiring(string token)
        {
            var expClaim = ParseClaimsFromJwt(token).FirstOrDefault(c =>
                c.Type == "exp" || c.Type == JwtRegisteredClaimNames.Exp);

            if (expClaim is null || !long.TryParse(expClaim.Value, out var expSeconds))
                return false;   

            // 30s margin: a token expiring mid-flight would just 401 on arrival anyway.
            return DateTimeOffset.FromUnixTimeSeconds(expSeconds)
                   <= DateTimeOffset.UtcNow.AddSeconds(30);
        }

        private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(jwt);
            return token.Claims;
        }
    }

}
