using Microsoft.Extensions.Logging.Abstractions;
using Plantitask.Core.Models;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;

namespace Plantitask.Tests.Services
{
    public class RedisServiceTests : RedisTestBase
    {
        private static readonly Guid UserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        private static readonly Guid OtherUserId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

        private readonly RedisService _sut;

        public RedisServiceTests(RedisFixture fixture) : base(fixture)
        {
            _sut = new RedisService(Connection, NullLogger<RedisService>.Instance);
        }

        private static RefreshTokenModel Token(Guid userId, string hash) => new()
        {
            UserId = userId,
            TokenHash = hash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByIp = "203.0.113.7"
        };

        [Fact]
        public async Task SetRefreshTokenAsync_RoundTripsTheWholeModel()
        {
            var model = Token(UserId, "hash-one");

            await _sut.SetRefreshTokenAsync("hash-one", model, TimeSpan.FromDays(7));
            var read = await _sut.GetRefreshTokenAsync("hash-one");

            Assert.NotNull(read);
            Assert.Equal(UserId, read.UserId);
            Assert.Equal("hash-one", read.TokenHash);
            Assert.Equal("203.0.113.7", read.CreatedByIp);
            Assert.False(read.IsRevoked);
            Assert.Null(read.RevokedAt);
            Assert.Equal(model.ExpiresAt, read.ExpiresAt, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task GetRefreshTokenAsync_ReturnsNullForAHashItHasNeverSeen()
        {
            Assert.Null(await _sut.GetRefreshTokenAsync("never-stored"));
        }

        /// <summary>
        /// The plaintext token never reaches Redis. Only its hash is ever used as a key, so a
        /// dump of the key space hands an attacker nothing they can present as a credential.
        /// </summary>
        [Fact]
        public async Task SetRefreshTokenAsync_KeysTheTokenByItsHashAndNothingElse()
        {
            await _sut.SetRefreshTokenAsync("the-hash", Token(UserId, "the-hash"), TimeSpan.FromDays(7));

            Assert.True(await Database.KeyExistsAsync("refresh_token:the-hash"));
        }

        [Fact]
        public async Task SetRefreshTokenAsync_GivesTheTokenAndTheUserSetATimeToLive()
        {
            await _sut.SetRefreshTokenAsync("hash-one", Token(UserId, "hash-one"), TimeSpan.FromHours(2));

            var tokenTtl = await Database.KeyTimeToLiveAsync("refresh_token:hash-one");
            var setTtl = await Database.KeyTimeToLiveAsync($"user_tokens:{UserId}");

            Assert.NotNull(tokenTtl);
            Assert.InRange(tokenTtl.Value, TimeSpan.FromMinutes(119), TimeSpan.FromHours(2));
            Assert.NotNull(setTtl);
            Assert.InRange(setTtl.Value, TimeSpan.FromMinutes(119), TimeSpan.FromHours(2));
        }

        /// <summary>
        /// The per user set is what makes revoke all possible without scanning the key space,
        /// which on a real Redis is the difference between an O(1) logout everywhere and a
        /// blocking scan of every key on the server.
        /// </summary>
        [Fact]
        public async Task SetRefreshTokenAsync_TracksEveryTokenHashInTheOwnersSet()
        {
            await _sut.SetRefreshTokenAsync("hash-one", Token(UserId, "hash-one"), TimeSpan.FromDays(7));
            await _sut.SetRefreshTokenAsync("hash-two", Token(UserId, "hash-two"), TimeSpan.FromDays(7));

            var members = await Database.SetMembersAsync($"user_tokens:{UserId}");

            Assert.Equal(2, members.Length);
            Assert.Contains("hash-one", members.Select(m => m.ToString()));
            Assert.Contains("hash-two", members.Select(m => m.ToString()));
        }

        [Fact]
        public async Task RevokeAllUserTokensAsync_RemovesEveryTokenAndTheSetItself()
        {
            await _sut.SetRefreshTokenAsync("hash-one", Token(UserId, "hash-one"), TimeSpan.FromDays(7));
            await _sut.SetRefreshTokenAsync("hash-two", Token(UserId, "hash-two"), TimeSpan.FromDays(7));

            await _sut.RevokeAllUserTokensAsync(UserId);

            Assert.Null(await _sut.GetRefreshTokenAsync("hash-one"));
            Assert.Null(await _sut.GetRefreshTokenAsync("hash-two"));
            Assert.False(await Database.KeyExistsAsync($"user_tokens:{UserId}"));
        }

        [Fact]
        public async Task RevokeAllUserTokensAsync_LeavesEveryOtherUsersSessionsAlone()
        {
            await _sut.SetRefreshTokenAsync("mine", Token(UserId, "mine"), TimeSpan.FromDays(7));
            await _sut.SetRefreshTokenAsync("theirs", Token(OtherUserId, "theirs"), TimeSpan.FromDays(7));

            await _sut.RevokeAllUserTokensAsync(UserId);

            Assert.Null(await _sut.GetRefreshTokenAsync("mine"));
            Assert.NotNull(await _sut.GetRefreshTokenAsync("theirs"));
        }

        [Fact]
        public async Task RevokeAllUserTokensAsync_IsHarmlessForAUserWithNoSessions()
        {
            var thrown = await Record.ExceptionAsync(() => _sut.RevokeAllUserTokensAsync(Guid.NewGuid()));

            Assert.Null(thrown);
        }

        /// <summary>
        /// Rotation marks rather than deletes, and this is why. The dead token stays readable so
        /// that a later attempt to use it can be recognised as a replay instead of looking like
        /// an unknown token, which is indistinguishable from an expired one.
        /// </summary>
        [Fact]
        public async Task MarkRefreshTokenRevokedAsync_LeavesTheTokenReadableAndFlaggedRatherThanGone()
        {
            await _sut.SetRefreshTokenAsync("rotated", Token(UserId, "rotated"), TimeSpan.FromDays(7));

            await _sut.MarkRefreshTokenRevokedAsync("rotated");

            var read = await _sut.GetRefreshTokenAsync("rotated");

            Assert.NotNull(read);
            Assert.True(read.IsRevoked);
            Assert.NotNull(read.RevokedAt);
        }

        /// <summary>
        /// The revoked copy has to keep the original expiry. Write it back with a fresh TTL and
        /// the replay evidence would outlive the token it belongs to, or worse, never expire.
        /// </summary>
        [Fact]
        public async Task MarkRefreshTokenRevokedAsync_PreservesTheRemainingTimeToLive()
        {
            await _sut.SetRefreshTokenAsync("rotated", Token(UserId, "rotated"), TimeSpan.FromHours(2));

            await _sut.MarkRefreshTokenRevokedAsync("rotated");

            var ttl = await Database.KeyTimeToLiveAsync("refresh_token:rotated");

            Assert.NotNull(ttl);
            Assert.InRange(ttl.Value, TimeSpan.FromMinutes(118), TimeSpan.FromHours(2));
        }

        [Fact]
        public async Task MarkRefreshTokenRevokedAsync_CreatesNothingForAnUnknownHash()
        {
            await _sut.MarkRefreshTokenRevokedAsync("never-stored");

            Assert.False(await Database.KeyExistsAsync("refresh_token:never-stored"));
        }

        /// <summary>
        /// Logout deletes outright, which is the opposite of rotation. Nothing is being detected
        /// here so there is no reason to keep the record around.
        /// </summary>
        [Fact]
        public async Task DeleteRefreshTokenAsync_RemovesTheTokenCompletely()
        {
            await _sut.SetRefreshTokenAsync("logged-out", Token(UserId, "logged-out"), TimeSpan.FromDays(7));

            await _sut.DeleteRefreshTokenAsync("logged-out");

            Assert.Null(await _sut.GetRefreshTokenAsync("logged-out"));
            Assert.False(await Database.KeyExistsAsync("refresh_token:logged-out"));
        }

        [Fact]
        public async Task DeleteRefreshTokenAsync_AlsoTakesTheHashOutOfTheOwnersSet()
        {
            await _sut.SetRefreshTokenAsync("kept", Token(UserId, "kept"), TimeSpan.FromDays(7));
            await _sut.SetRefreshTokenAsync("dropped", Token(UserId, "dropped"), TimeSpan.FromDays(7));

            await _sut.DeleteRefreshTokenAsync("dropped");

            var members = await Database.SetMembersAsync($"user_tokens:{UserId}");

            Assert.Equal("kept", Assert.Single(members).ToString());
        }

        [Fact]
        public async Task DeleteRefreshTokenAsync_IsHarmlessForAnUnknownHash()
        {
            var thrown = await Record.ExceptionAsync(() => _sut.DeleteRefreshTokenAsync("never-stored"));

            Assert.Null(thrown);
        }

        [Fact]
        public async Task StoreVerificationCodeAsync_MakesTheHashReadableBack()
        {
            await _sut.StoreVerificationCodeAsync("user@example.com", "bcrypt-hash", TimeSpan.FromMinutes(15));

            Assert.Equal("bcrypt-hash", await _sut.GetVerificationCodeHashAsync("user@example.com"));
        }

        /// <summary>
        /// Email addresses are case insensitive as mailboxes, so a code requested as one casing
        /// has to be findable under any other. Anything else lets a retyped address silently miss
        /// the code that was just sent.
        /// </summary>
        [Fact]
        public async Task VerificationCodesAreFoundRegardlessOfTheCasingOfTheEmail()
        {
            await _sut.StoreVerificationCodeAsync("User@Example.COM", "bcrypt-hash", TimeSpan.FromMinutes(15));

            Assert.Equal("bcrypt-hash", await _sut.GetVerificationCodeHashAsync("user@example.com"));
            Assert.Equal("bcrypt-hash", await _sut.GetVerificationCodeHashAsync("USER@EXAMPLE.COM"));
        }

        [Fact]
        public async Task GetVerificationCodeHashAsync_ReturnsNullWhenNoCodeWasStored()
        {
            Assert.Null(await _sut.GetVerificationCodeHashAsync("nobody@example.com"));
        }

        [Fact]
        public async Task StoreVerificationCodeAsync_GivesTheKeyATimeToLive()
        {
            await _sut.StoreVerificationCodeAsync("user@example.com", "bcrypt-hash", TimeSpan.FromMinutes(15));

            var ttl = await Database.KeyTimeToLiveAsync("verification:user@example.com");

            Assert.NotNull(ttl);
            Assert.InRange(ttl.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        }

        /// <summary>
        /// A used code has to verify exactly like a missing one. Returning the hash after use
        /// would let the same code be presented twice.
        /// </summary>
        [Fact]
        public async Task AUsedCodeReadsBackAsIfItWereNotThere()
        {
            await _sut.StoreVerificationCodeAsync("user@example.com", "bcrypt-hash", TimeSpan.FromMinutes(15));

            await _sut.MarkVerificationCodeUsedAsync("user@example.com");

            Assert.Null(await _sut.GetVerificationCodeHashAsync("user@example.com"));
        }

        [Fact]
        public async Task MarkVerificationCodeUsedAsync_IsWhatMakesTheEmailCountAsVerified()
        {
            await _sut.StoreVerificationCodeAsync("user@example.com", "bcrypt-hash", TimeSpan.FromMinutes(15));

            Assert.False(await _sut.IsEmailVerifiedAsync("user@example.com"));

            await _sut.MarkVerificationCodeUsedAsync("user@example.com");

            Assert.True(await _sut.IsEmailVerifiedAsync("user@example.com"));
        }

        [Fact]
        public async Task IsEmailVerifiedAsync_IsFalseForAnEmailWithNoCodeAtAll()
        {
            Assert.False(await _sut.IsEmailVerifiedAsync("nobody@example.com"));
        }

        /// <summary>
        /// The KeyExists guard in MarkVerificationCodeUsedAsync is load bearing. A hash write
        /// against a key that has expired recreates it with no expiry at all, which would leave
        /// the address permanently marked verified. Marking an email that was never issued a code
        /// must therefore create nothing.
        /// </summary>
        [Fact]
        public async Task MarkVerificationCodeUsedAsync_CreatesNoKeyForAnEmailThatNeverHadACode()
        {
            await _sut.MarkVerificationCodeUsedAsync("nobody@example.com");

            Assert.False(await Database.KeyExistsAsync("verification:nobody@example.com"));
            Assert.False(await _sut.IsEmailVerifiedAsync("nobody@example.com"));
        }

        [Fact]
        public async Task MarkVerificationCodeUsedAsync_DoesNotDropTheExpiryOfALiveKey()
        {
            await _sut.StoreVerificationCodeAsync("user@example.com", "bcrypt-hash", TimeSpan.FromMinutes(15));

            await _sut.MarkVerificationCodeUsedAsync("user@example.com");

            var ttl = await Database.KeyTimeToLiveAsync("verification:user@example.com");

            Assert.NotNull(ttl);
            Assert.InRange(ttl.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        }

        /// <summary>
        /// The issue time feeds the resend throttle, so it has to survive the round trip through
        /// Redis as a real instant rather than losing its kind or its precision.
        /// </summary>
        [Fact]
        public async Task GetVerificationCodeCreatedAtAsync_RoundTripsTheIssueTimeAsUtc()
        {
            var before = DateTime.UtcNow;

            await _sut.StoreVerificationCodeAsync("user@example.com", "bcrypt-hash", TimeSpan.FromMinutes(15));

            var createdAt = await _sut.GetVerificationCodeCreatedAtAsync("user@example.com");

            Assert.NotNull(createdAt);
            Assert.Equal(DateTimeKind.Utc, createdAt.Value.Kind);
            Assert.InRange(createdAt.Value, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
        }

        [Fact]
        public async Task GetVerificationCodeCreatedAtAsync_ReturnsNullWhenNoCodeWasStored()
        {
            Assert.Null(await _sut.GetVerificationCodeCreatedAtAsync("nobody@example.com"));
        }

        /// <summary>
        /// Reissuing replaces the previous code rather than leaving both valid, and the fresh
        /// entry starts unused even if the one before it had been consumed.
        /// </summary>
        [Fact]
        public async Task StoringASecondCodeReplacesTheFirstAndResetsTheUsedFlag()
        {
            await _sut.StoreVerificationCodeAsync("user@example.com", "first-hash", TimeSpan.FromMinutes(15));
            await _sut.MarkVerificationCodeUsedAsync("user@example.com");

            await _sut.StoreVerificationCodeAsync("user@example.com", "second-hash", TimeSpan.FromMinutes(15));

            Assert.Equal("second-hash", await _sut.GetVerificationCodeHashAsync("user@example.com"));
            Assert.False(await _sut.IsEmailVerifiedAsync("user@example.com"));
        }
    }
}
