using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Models;

namespace Plantitask.Infrastructure.Services
{
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

        public async Task<bool> ExistsAsync(string key)
        {
                return await _db.KeyExistsAsync(key);
        }

        public async Task<T?> GetAsync<T>(string key)
        {
                var json = await _db.StringGetAsync(key);

                if (json.IsNullOrEmpty)
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(json!);
        }
        public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
        {

                var json = JsonSerializer.Serialize(value);
                await _db.StringSetAsync(key, json, expiration, When.NotExists);

        }

        public async Task RemoveAsync(string key)
        {

                await _db.KeyDeleteAsync(key);
            
        }

        public async Task<RefreshTokenModel?> GetRefreshTokenAsync(string token)
        {
                var key = GetRefreshTokenKey(token);
                var json = await _db.StringGetAsync(key);

                if (json.IsNullOrEmpty)
                {
                    return null;
                }

                return JsonSerializer.Deserialize<RefreshTokenModel>(json!);
        }

        public async Task SetRefreshTokenAsync(string token, RefreshTokenModel model, TimeSpan expiration)
        {
                var key = GetRefreshTokenKey(token);
                var json = JsonSerializer.Serialize(model);
                await _db.StringSetAsync(key, json, expiration);

                var userTokensKey = GetUserTokensKey(model.UserId);
                await _db.SetAddAsync(userTokensKey, token);
                _logger.LogInformation("Refresh token stored in Redis for user {UserId}", model.UserId);
            
        }

        public async Task RevokeAllUserTokensAsync(Guid userId)
        {
                var userTokensKey = GetUserTokensKey(userId);

                var tokens = await _db.SetMembersAsync(userTokensKey);

                foreach(var token in tokens)
                {
                    var key = GetRefreshTokenKey(token!);
                    await _db.KeyDeleteAsync(key);
                }

                await _db.KeyDeleteAsync(userTokensKey);
                _logger.LogInformation("Refresh token revoked");
        }

        public async Task RevokeRefreshTokenAsync(string token)
        {
                var key = GetRefreshTokenKey(token);

                var model = await GetRefreshTokenAsync(token);

                await _db.KeyDeleteAsync(key);

                if (model != null)
                {
                    var userTokensKey = GetUserTokensKey(model.UserId);
                    await _db.SetRemoveAsync(userTokensKey, token);
                }

                _logger.LogInformation("Refresh token revoked");
            
        }


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

        public async Task MarkVerificationCodeUsedAsync(string email)
        {
            var key = $"verification:{email.ToLower()}";

            if (!await _db.KeyExistsAsync(key))
                return;

            await _db.HashSetAsync(key, "IsUsed", "true");

            _logger.LogInformation("Verification code marked as used for {Email}", email);
        }

        public async Task<bool> IsEmailVerifiedAsync(string email)
        {
            var key = $"verification:{email.ToLower()}";
            var value = await _db.HashGetAsync(key, "IsUsed");

            if (value.IsNullOrEmpty)
                return false;

            return string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<DateTime?> GetVerificationCodeCreatedAtAsync(string email)
        {
            var key = $"verification:{email.ToLower()}";
            var value = await _db.HashGetAsync(key, "CreatedAt");

            if (value.IsNullOrEmpty)
                return null;

            return DateTime.Parse(value.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind);
        }


        public static string GetRefreshTokenKey(string token) => $"refresh_token:{token}";
        public static string GetUserTokensKey(Guid userId) => $"user_tokens:{userId}";
    }
}
