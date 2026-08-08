using System;
using System.Text;
using Plantitask.Core.Common;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// Issues the two token kinds: short-lived signed JWTs for requests and long random
    /// strings for refresh. Access tokens are deliberately short because they cannot be
    /// revoked - only the refresh token's lifetime is ever extended.
    /// </summary>
    public class JwtTokenGenerator : IJwtTokenGenerator
    {
        private readonly JwtSettings _jwtSettings;
        public JwtTokenGenerator(IOptions<JwtSettings> jwtSettings)
        {
            _jwtSettings = jwtSettings.Value;
        }

        /// <summary>
        /// A signed HMAC-SHA256 JWT carrying the user's id, email and username, expiring after
        /// the configured minutes. The jti claim makes every token unique even inside one second.
        /// </summary>
        public string GenerateAccessToken(User user)
        {
            var claims = new[]
            {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
    };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var expiry = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryInMinutes);

            var token = new JwtSecurityToken(
                issuer: _jwtSettings.Issuer,
                audience: _jwtSettings.Audience,
                claims: claims,
                expires: expiry,
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// 64 random bytes (512 bits) as Base64. High-entropy on purpose - that is what makes
        /// plain SHA-256 the right storage hash for it.
        /// </summary>
        public string GenerateRefreshToken()
        {
            var bytes = new byte[64];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes);
        }
    }
}
