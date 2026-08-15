using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Plantitask.Core.Common;
using Plantitask.Core.Entities;
using Plantitask.Infrastructure.Services;

namespace Plantitask.Tests.Services
{
    public class JwtTokenGeneratorTests
    {
        private const string Secret = "a-test-signing-secret-long-enough-for-hmac-sha256";

        private static readonly JwtSettings Settings = new()
        {
            Secret = Secret,
            Issuer = "plantitask-tests",
            Audience = "plantitask-clients",
            AccessTokenExpiryInMinutes = 15,
            RefreshTokenExpiryInDays = 7
        };

        private readonly JwtTokenGenerator _sut = new(Options.Create(Settings));

        private static readonly User User = new()
        {
            Id = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            UserName = "lead",
            Email = "lead@example.com",
            PasswordHash = "irrelevant"
        };

        private JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

        [Fact]
        public void GenerateAccessToken_CarriesTheIdentityClaimsTheApiReadsBack()
        {
            var token = Read(_sut.GenerateAccessToken(User));

            Assert.Equal(User.Id.ToString(), token.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
            Assert.Equal(User.Email, token.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
            Assert.Equal(User.UserName, token.Claims.Single(c => c.Type == JwtRegisteredClaimNames.UniqueName).Value);
        }

        [Fact]
        public void GenerateAccessToken_StampsTheConfiguredIssuerAndAudience()
        {
            var token = Read(_sut.GenerateAccessToken(User));

            Assert.Equal("plantitask-tests", token.Issuer);
            Assert.Contains("plantitask-clients", token.Audiences);
        }

        [Fact]
        public void GenerateAccessToken_ExpiresAfterTheConfiguredMinutes()
        {
            var before = DateTime.UtcNow;

            var token = Read(_sut.GenerateAccessToken(User));

            var expected = before.AddMinutes(Settings.AccessTokenExpiryInMinutes);
            Assert.Equal(expected, token.ValidTo, TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Two tokens issued for the same user inside the same second would otherwise be byte
        /// identical, because every other claim is derived from the user. The jti is what keeps
        /// them distinguishable.
        /// </summary>
        [Fact]
        public void GenerateAccessToken_GivesEveryTokenItsOwnJti()
        {
            var first = Read(_sut.GenerateAccessToken(User));
            var second = Read(_sut.GenerateAccessToken(User));

            var firstJti = first.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
            var secondJti = second.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

            Assert.NotEqual(firstJti, secondJti);
        }

        /// <summary>
        /// Reading a token proves nothing about its signature, since anyone can write claims.
        /// This validates it the way the API pipeline does, with the configured key.
        /// MapInboundClaims is false here because Program.cs sets it false. Leave it on and the
        /// legacy handler rewrites sub to ClaimTypes.NameIdentifier, at which point
        /// BaseApiController's lookup for JwtRegisteredClaimNames.Sub finds nothing. The test
        /// has to read the token under the same setting the API reads it under.
        /// </summary>
        [Fact]
        public void GenerateAccessToken_IsSignedWithTheConfiguredSecretAndKeepsTheSubClaimName()
        {
            var token = _sut.GenerateAccessToken(User);

            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                ValidIssuer = Settings.Issuer,
                ValidAudience = Settings.Audience,
                ValidateLifetime = true
            };

            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, parameters, out _);

            Assert.Equal(User.Id.ToString(), principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        }

        [Fact]
        public void GenerateAccessToken_FailsValidationUnderADifferentSecret()
        {
            var token = _sut.GenerateAccessToken(User);

            var parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes("a-completely-different-secret-of-sufficient-length")),
                ValidIssuer = Settings.Issuer,
                ValidAudience = Settings.Audience
            };

            Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(
                () => new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _));
        }

        /// <summary>
        /// Refresh tokens are 64 random bytes rather than anything derived, which is what makes
        /// plain SHA-256 the right storage hash for them elsewhere. Base64 of 64 bytes is 88
        /// characters, so the length is a cheap proxy for the entropy actually being there.
        /// </summary>
        [Fact]
        public void GenerateRefreshToken_IsSixtyFourRandomBytesInBase64()
        {
            var token = _sut.GenerateRefreshToken();

            Assert.Equal(88, token.Length);
            Assert.Equal(64, Convert.FromBase64String(token).Length);
        }

        [Fact]
        public void GenerateRefreshToken_DoesNotRepeatItself()
        {
            var tokens = Enumerable.Range(0, 500).Select(_ => _sut.GenerateRefreshToken()).ToList();

            Assert.Equal(tokens.Count, tokens.Distinct().Count());
        }
    }
}
