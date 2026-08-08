using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Models;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// Redis-backed storage for refresh tokens and email verification codes. Tokens are keyed
    /// by their SHA-256 hash - the plaintext never reaches Redis - and a per-user set makes
    /// revoke-all possible without scanning keys.
    /// </summary>
    public class RedisService : IRedisService
    {

        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;
        private readonly ILogger<RedisService> _logger;

        public RedisService(IConnectionMultiplexer redis, ILogger<RedisService> logger)
        {
            _redis = redis;
            _db = redis.GetDatabase();
            _logger = logger;
        }


        /// <summary>Looks a token up by hash; null means unknown or already expired out of Redis.</summary>
        public async Task<RefreshTokenModel?> GetRefreshTokenAsync(string tokenHash)
        {
                var key = GetRefreshTokenKey(tokenHash);
                var json = await _db.StringGetAsync(key);

                if (json.IsNullOrEmpty)
                {
                    return null;
                }

                return JsonSerializer.Deserialize<RefreshTokenModel>(json.ToString());
        }

        /// <summary>
        /// Stores a token under its hash and adds the hash to the owner's token set. The set's
        /// TTL is pushed out to match the newest token, so it lives exactly as long as the
        /// longest-lived token in it.
        /// </summary>
        public async Task SetRefreshTokenAsync(string tokenHash, RefreshTokenModel model, TimeSpan expiration)
        {
                var key = GetRefreshTokenKey(tokenHash);
                var json = JsonSerializer.Serialize(model);
                await _db.StringSetAsync(key, json, expiration);

                var userTokensKey = GetUserTokensKey(model.UserId);
                await _db.SetAddAsync(userTokensKey, tokenHash);
                await _db.KeyExpireAsync(userTokensKey, expiration);
                _logger.LogDebug("Stored refresh token for user {UserId}", model.UserId);
            
        }

        /// <summary>
        /// Kills every session the user has: deletes each token in their set, then the set
        /// itself. Called on credential changes and on refresh-token replay.
        /// </summary>
        public async Task RevokeAllUserTokensAsync(Guid userId)
        {
                var userTokensKey = GetUserTokensKey(userId);

                var tokens = await _db.SetMembersAsync(userTokensKey);

                foreach(var tokenHash in tokens)
                {
                    var key = GetRefreshTokenKey(tokenHash!);
                    await _db.KeyDeleteAsync(key);
                }

                await _db.KeyDeleteAsync(userTokensKey);
                _logger.LogDebug("Revoked all refresh tokens for user {UserId}", userId);
        }

        /// <summary>
        /// Flags a rotated token as revoked while preserving its remaining TTL. Keeping the dead
        /// token around is the point - its reappearance later is how replay gets detected.
        /// </summary>
        public async Task MarkRefreshTokenRevokedAsync(string tokenHash)
        {
            var key = GetRefreshTokenKey(tokenHash);
            var model = await GetRefreshTokenAsync(tokenHash);
            if (model == null) return;

            model.IsRevoked = true;
            model.RevokedAt = DateTime.UtcNow;
            var remainingTtl = await _db.KeyTimeToLiveAsync(key) ?? TimeSpan.FromDays(7);
            await _db.StringSetAsync(key, JsonSerializer.Serialize(model), remainingTtl);
            _logger.LogDebug("Marked refresh token revoked for user {UserId}", model.UserId);
        }
        /// <summary>
        /// Removes a token completely - logout only. Rotation goes through
        /// <see cref="MarkRefreshTokenRevokedAsync"/> instead so replay stays detectable.
        /// </summary>
        public async Task DeleteRefreshTokenAsync(string tokenHash)
        {
            var model = await GetRefreshTokenAsync(tokenHash);
            await _db.KeyDeleteAsync(GetRefreshTokenKey(tokenHash));
            if (model != null)
            {
                await _db.SetRemoveAsync(GetUserTokensKey(model.UserId), tokenHash);
                _logger.LogDebug("Deleted refresh token for user {UserId}", model.UserId);
            }
        }

        /// <summary>
        /// Stores a verification code's BCrypt hash in a per-email Redis hash with a TTL. The
        /// hash also carries CreatedAt (for resend throttling) and the IsUsed flag.
        /// </summary>
        public async Task StoreVerificationCodeAsync(string email, string codeHash, TimeSpan expiration)
        {
            var key = $"verification:{email.ToLower()}";

            var entries = new HashEntry[]
            {
                new("CodeHash", codeHash),
                new("CreatedAt", DateTime.UtcNow.ToString("O")),
                new("IsUsed", "false")
            };

            await _db.HashSetAsync(key, entries);
            await _db.KeyExpireAsync(key, expiration);

            _logger.LogInformation("Verification code stored for {Email}", email);
        }


        /// <summary>
        /// The stored code hash, or null when there is none or the code was already used - a
        /// used code must verify exactly like a missing one.
        /// </summary>
        public async Task<string?> GetVerificationCodeHashAsync(string email)
        {
            var key = $"verification:{email.ToLower()}";

            var values = await _db.HashGetAsync(key, new RedisValue[] { "IsUsed", "CodeHash" });

            if (values[0].IsNullOrEmpty || values[1].IsNullOrEmpty)
                return null;

            if (string.Equals(values[0].ToString(), "true", StringComparison.OrdinalIgnoreCase))
                return null;

            return values[1].ToString();
        }

        /// <summary>
        /// Flips IsUsed after a successful verification, which is what
        /// <see cref="IsEmailVerifiedAsync"/> reads during registration.
        /// </summary>
        public async Task MarkVerificationCodeUsedAsync(string email)
        {
            var key = $"verification:{email.ToLower()}";

            // Guard is load bearing: HashSet on an expired key recreates it WITHOUT A TTL
            if (!await _db.KeyExistsAsync(key))
                return;

            await _db.HashSetAsync(key, "IsUsed", "true");

            _logger.LogInformation("Verification code marked as used for {Email}", email);
        }

        /// <summary>
        /// Whether this email passed code verification recently. "Used" means verified here, and
        /// the answer expires with the Redis key - verification is a window, not a permanent fact.
        /// </summary>
        public async Task<bool> IsEmailVerifiedAsync(string email)
        {
            var key = $"verification:{email.ToLower()}";
            var value = await _db.HashGetAsync(key, "IsUsed");

            if (value.IsNullOrEmpty)
                return false;

            return string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>When the current code was issued - feeds the one-minute resend throttle.</summary>
        public async Task<DateTime?> GetVerificationCodeCreatedAtAsync(string email)
        {
            var key = $"verification:{email.ToLower()}";
            var value = await _db.HashGetAsync(key, "CreatedAt");

            if (value.IsNullOrEmpty)
                return null;

            return DateTime.Parse(value.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
        }


        public static string GetRefreshTokenKey(string tokenHash) => $"refresh_token:{tokenHash}";
        public static string GetUserTokensKey(Guid userId) => $"user_tokens:{userId}";
    }
}
