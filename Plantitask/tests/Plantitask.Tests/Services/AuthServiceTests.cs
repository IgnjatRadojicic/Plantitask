using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Auth;
using Plantitask.Core.DTO.Users;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Models;
using Plantitask.Infrastructure.Security;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class AuthServiceTests : DbTestBase
    {
        private const string AccessToken = "access-token";
        private const string NewRefreshToken = "new-refresh-token";

        private readonly Mock<IPasswordHasher> _hasher = new();
        private readonly Mock<IJwtTokenGenerator> _tokens = new();
        private readonly Mock<IEmailService> _email = new();
        private readonly Mock<IRedisService> _redis = new();
        private readonly List<string> _callOrder = [];

        public AuthServiceTests(PostgresFixture fixture) : base(fixture)
        {
            // A reversible stand in for BCrypt. The real hasher costs a third of a second a call
            // and none of these tests are about how a password is hashed.
            _hasher.Setup(h => h.HashPassword(It.IsAny<string>()))
                .Returns<string>(plain => $"hashed:{plain}");
            _hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((plain, hash) => hash == $"hashed:{plain}");
            _hasher.Setup(h => h.NeedsRehash(It.IsAny<string>())).Returns(false);
            _hasher.Setup(h => h.DummyHash).Returns("hashed:$$dummy$$");

            _tokens.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns(AccessToken);
            _tokens.Setup(t => t.GenerateRefreshToken()).Returns(NewRefreshToken);

            _redis
                .Setup(r => r.SetRefreshTokenAsync(
                    It.IsAny<string>(), It.IsAny<RefreshTokenModel>(), It.IsAny<TimeSpan>()))
                .Callback(() => _callOrder.Add("store"))
                .Returns(Task.CompletedTask);

            _redis
                .Setup(r => r.RevokeAllUserTokensAsync(It.IsAny<Guid>()))
                .Callback(() => _callOrder.Add("revoke"))
                .Returns(Task.CompletedTask);

            _redis.Setup(r => r.IsEmailVerifiedAsync(It.IsAny<string>())).ReturnsAsync(true);
        }

        private AuthService NewSut(IApplicationDbContext context) => new(
            context,
            _hasher.Object,
            _tokens.Object,
            _email.Object,
            NullLogger<AuthService>.Instance,
            _redis.Object,
            Options.Create(new GoogleAuthSettings { ClientId = "google-client-id" }),
            Options.Create(new JwtSettings { RefreshTokenExpiryInDays = 7 }),
            Options.Create(new AppSettings { FrontendUrl = "https://app.plantitask.test" }));

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        /// <summary>The seeded users all carry a known plaintext so login can be exercised.</summary>
        private async Task SetPasswordAsync(Guid userId, string plaintext)
        {
            await using var db = NewContext();
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.PasswordHash = $"hashed:{plaintext}";
            user.IsEmailConfirmed = true;
            await db.SaveChangesAsync();
        }

        private async Task<User> ReadUserAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.Users.SingleAsync(u => u.Id == id);
        }

        private static RegisterDto NewRegistration(
            string email = "newcomer@example.com", string userName = "newcomer") => new()
            {
                Email = email,
                UserName = userName,
                Password = "Str0ng!pass",
                FirstName = "Ada",
                LastName = "Lovelace"
            };

        private static RefreshTokenModel StoredToken(
            Guid userId,
            bool revoked = false,
            DateTime? expiresAt = null,
            string ip = "203.0.113.7",
            DateTime? revokedAt = null) => new()
            {
                UserId = userId,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
                CreatedByIp = ip,
                IsRevoked = revoked,
                // MarkRefreshTokenRevokedAsync stamps both fields on the same line, so revoked
                // with no timestamp is a state production cannot reach. Default the stamp well
                // outside the grace window so "revoked" keeps meaning "replayed" by default.
                RevokedAt = revoked ? revokedAt ?? DateTime.UtcNow.AddMinutes(-5) : null
            };

        [Fact]
        public async Task RegisterAsync_RefusesAnEmailThatWasNeverVerified()
        {
            await SeedAsync();
            _redis.Setup(r => r.IsEmailVerifiedAsync(It.IsAny<string>())).ReturnsAsync(false);

            await using var act = NewContext();
            var result = await NewSut(act).RegisterAsync(NewRegistration(), "1.1.1.1");

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);

            await using var assert = NewContext();
            Assert.False(await assert.Users.AnyAsync(u => u.Email == "newcomer@example.com"));
        }

        [Fact]
        public async Task RegisterAsync_CreatesTheAccountAndSignsThemIn()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).RegisterAsync(NewRegistration(), "1.1.1.1");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(AccessToken, result.Value!.AccessToken);
            Assert.Equal(NewRefreshToken, result.Value.RefreshToken);
            Assert.Equal("newcomer", result.Value.UserName);

            await using var assert = NewContext();
            var created = await assert.Users.SingleAsync(u => u.Email == "newcomer@example.com");
            Assert.Equal("hashed:Str0ng!pass", created.PasswordHash);
            Assert.True(created.IsEmailConfirmed);

            _email.Verify(e => e.SendWelcomeEmailAsync("newcomer@example.com", "Ada"), Times.Once);
        }

        /// <summary>
        /// The refresh token is stored under its sha256 hash and the plaintext never reaches
        /// Redis, so a dump of the key space hands an attacker nothing presentable.
        /// </summary>
        [Fact]
        public async Task RegisterAsync_StoresTheRefreshTokenByItsHashAndNotItsPlaintext()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).RegisterAsync(NewRegistration(), "203.0.113.7");

            _redis.Verify(r => r.SetRefreshTokenAsync(
                TokenHasher.Sha256(NewRefreshToken),
                It.Is<RefreshTokenModel>(m => m.CreatedByIp == "203.0.113.7" && !m.IsRevoked),
                It.IsAny<TimeSpan>()), Times.Once);

            _redis.Verify(r => r.SetRefreshTokenAsync(
                NewRefreshToken, It.IsAny<RefreshTokenModel>(), It.IsAny<TimeSpan>()), Times.Never);
        }

        [Fact]
        public async Task RegisterAsync_NormalisesTheEmailAndTrimsTheUsername()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).RegisterAsync(
                NewRegistration(email: "  NewComer@Example.COM  ", userName: "  newcomer  "), "1.1.1.1");

            await using var assert = NewContext();
            var created = await assert.Users.SingleAsync(u => u.UserName == "newcomer");
            Assert.Equal("newcomer@example.com", created.Email);
        }

        [Fact]
        public async Task RegisterAsync_ReportsADuplicateEmailAndADuplicateUsernameDifferently()
        {
            await SeedAsync();

            await using var act = NewContext();
            var sut = NewSut(act);

            var byEmail = await sut.RegisterAsync(
                NewRegistration(email: "member@example.com", userName: "brandnew"), "1.1.1.1");
            var byName = await sut.RegisterAsync(
                NewRegistration(email: "brandnew@example.com", userName: "member"), "1.1.1.1");

            Assert.Equal("Conflict", byEmail.Error!.Code);
            Assert.Contains("email", byEmail.Error.Message);

            Assert.Equal("Conflict", byName.Error!.Code);
            Assert.Contains("username", byName.Error.Message);
        }

        [Fact]
        public async Task RegisterAsync_StillSucceedsWhenTheWelcomeEmailFails()
        {
            await SeedAsync();

            _email
                .Setup(e => e.SendWelcomeEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new EmailSendException("provider down"));

            await using var act = NewContext();
            var result = await NewSut(act).RegisterAsync(NewRegistration(), "1.1.1.1");

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            Assert.True(await assert.Users.AnyAsync(u => u.Email == "newcomer@example.com"));
        }

        /// <summary>
        /// An unknown email still costs a password verification against a dummy hash. Skipping it
        /// would make the unknown case measurably faster and turn login into an account oracle.
        /// </summary>
        [Fact]
        public async Task LoginAsync_VerifiesADummyHashForAnUnknownEmail()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).LoginAsync(
                new LoginDto { Email = "nobody@example.com", Password = "whatever" }, "1.1.1.1");

            Assert.True(result.IsFailure);
            Assert.Equal("Unauthorized", result.Error!.Code);

            _hasher.Verify(h => h.VerifyPassword("whatever", "hashed:$$dummy$$"), Times.Once);
        }

        /// <summary>
        /// Unknown email, wrong password and an unconfirmed account all answer identically. Any
        /// difference between them tells an attacker which accounts exist.
        /// </summary>
        [Fact]
        public async Task LoginAsync_AnswersIdenticallyForEveryKindOfFailure()
        {
            await SeedAsync();
            await SetPasswordAsync(MemberId, "Str0ng!pass");

            await using (var db = NewContext())
            {
                var lead = await db.Users.SingleAsync(u => u.Id == LeadId);
                lead.PasswordHash = "hashed:Str0ng!pass";
                lead.IsEmailConfirmed = false;
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var sut = NewSut(act);

            var unknown = await sut.LoginAsync(
                new LoginDto { Email = "nobody@example.com", Password = "Str0ng!pass" }, "1.1.1.1");
            var wrongPassword = await sut.LoginAsync(
                new LoginDto { Email = "member@example.com", Password = "wrong" }, "1.1.1.1");
            var unconfirmed = await sut.LoginAsync(
                new LoginDto { Email = "lead@example.com", Password = "Str0ng!pass" }, "1.1.1.1");

            Assert.Equal("Unauthorized", unknown.Error!.Code);
            Assert.Equal(unknown.Error.Message, wrongPassword.Error!.Message);
            Assert.Equal(unknown.Error.Message, unconfirmed.Error!.Message);
        }

        [Fact]
        public async Task LoginAsync_IssuesTokensAndStampsTheLoginTime()
        {
            await SeedAsync();
            await SetPasswordAsync(MemberId, "Str0ng!pass");

            await using var act = NewContext();
            var result = await NewSut(act).LoginAsync(
                new LoginDto { Email = "member@example.com", Password = "Str0ng!pass" }, "1.1.1.1");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(MemberId, result.Value!.UserId);
            Assert.NotNull((await ReadUserAsync(MemberId)).LastLoginAt);
        }

        [Fact]
        public async Task LoginAsync_FindsTheAccountWhateverCasingTheEmailArrivesIn()
        {
            await SeedAsync();
            await SetPasswordAsync(MemberId, "Str0ng!pass");

            await using var act = NewContext();
            var result = await NewSut(act).LoginAsync(
                new LoginDto { Email = "  Member@Example.COM ", Password = "Str0ng!pass" }, "1.1.1.1");

            Assert.True(result.IsSuccess, result.Error?.Message);
        }

        /// <summary>
        /// Login is the only moment the plaintext is in hand, so an outdated work factor is
        /// upgraded there rather than forcing a password reset.
        /// </summary>
        [Fact]
        public async Task LoginAsync_RehashesAPasswordStoredAtAnOlderWorkFactor()
        {
            await SeedAsync();
            await SetPasswordAsync(MemberId, "Str0ng!pass");

            _hasher.Setup(h => h.NeedsRehash("hashed:Str0ng!pass")).Returns(true);
            _hasher.Setup(h => h.HashPassword("Str0ng!pass")).Returns("rehashed:Str0ng!pass");

            await using var act = NewContext();
            await NewSut(act).LoginAsync(
                new LoginDto { Email = "member@example.com", Password = "Str0ng!pass" }, "1.1.1.1");

            Assert.Equal("rehashed:Str0ng!pass", (await ReadUserAsync(MemberId)).PasswordHash);
        }

        [Fact]
        public async Task RefreshTokenAsync_RejectsATokenRedisHasNeverSeen()
        {
            await SeedAsync();
            _redis.Setup(r => r.GetRefreshTokenAsync(It.IsAny<string>())).ReturnsAsync((RefreshTokenModel?)null);

            await using var act = NewContext();
            var result = await NewSut(act).RefreshTokenAsync("whatever");

            Assert.True(result.IsFailure);
            Assert.Equal("Unauthorized", result.Error!.Code);
        }

        /// <summary>
        /// A revoked token coming back is theft's signature. Rotation marks rather than deletes
        /// precisely so this is distinguishable from an unknown token, and the response is to end
        /// every session the user has rather than only refuse this one.
        /// </summary>
        [Fact]
        public async Task RefreshTokenAsync_TreatsAReplayedTokenAsTheftAndEndsEverySession()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetRefreshTokenAsync(It.IsAny<string>()))
                .ReturnsAsync(StoredToken(MemberId, revoked: true));

            await using var act = NewContext();
            var result = await NewSut(act).RefreshTokenAsync("replayed");

            Assert.True(result.IsFailure);
            Assert.Equal("Unauthorized", result.Error!.Code);

            _redis.Verify(r => r.RevokeAllUserTokensAsync(MemberId), Times.Once);
        }

        /// <summary>
        /// Two tabs that got past the browser lock hand in the same token, and the loser must not
        /// be read as a thief. Elapsed time since rotation is the only thing separating the two,
        /// so a token revoked moments ago earns a 409 and every session stays alive.
        /// </summary>
        [Fact]
        public async Task RefreshTokenAsync_InsideTheRotationGrace_ReportsAConflictAndKeepsSessions()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetRefreshTokenAsync(It.IsAny<string>()))
                .ReturnsAsync(StoredToken(
                    MemberId, revoked: true, revokedAt: DateTime.UtcNow.AddSeconds(-1)));

            await using var act = NewContext();
            var result = await NewSut(act).RefreshTokenAsync("lost-the-race");

            Assert.True(result.IsFailure);
            Assert.Equal("Conflict", result.Error!.Code);

            _redis.Verify(r => r.RevokeAllUserTokensAsync(It.IsAny<Guid>()), Times.Never);
        }

        /// <summary>
        /// The grace window is a clock, not an amnesty. A token revoked longer ago than the window
        /// is still theft's signature and still ends every session.
        /// </summary>
        [Fact]
        public async Task RefreshTokenAsync_JustOutsideTheRotationGrace_StillEndsEverySession()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetRefreshTokenAsync(It.IsAny<string>()))
                .ReturnsAsync(StoredToken(
                    MemberId, revoked: true, revokedAt: DateTime.UtcNow.AddSeconds(-30)));

            await using var act = NewContext();
            var result = await NewSut(act).RefreshTokenAsync("replayed-later");

            Assert.True(result.IsFailure);
            Assert.Equal("Unauthorized", result.Error!.Code);

            _redis.Verify(r => r.RevokeAllUserTokensAsync(MemberId), Times.Once);
        }

        /// <summary>
        /// A revoked token with no RevokedAt cannot be produced by the rotation path, so it is a
        /// data anomaly. It must fail closed rather than fall into the grace branch, because an
        /// unknown revocation time is not evidence of innocence.
        /// </summary>
        [Fact]
        public async Task RefreshTokenAsync_RevokedWithNoTimestamp_FailsClosed()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetRefreshTokenAsync(It.IsAny<string>()))
                .ReturnsAsync(new RefreshTokenModel
                {
                    UserId = MemberId,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-5),
                    ExpiresAt = DateTime.UtcNow.AddDays(7),
                    CreatedByIp = "203.0.113.7",
                    IsRevoked = true,
                    RevokedAt = null
                });

            await using var act = NewContext();
            var result = await NewSut(act).RefreshTokenAsync("anomalous");

            Assert.True(result.IsFailure);
            Assert.Equal("Unauthorized", result.Error!.Code);

            _redis.Verify(r => r.RevokeAllUserTokensAsync(MemberId), Times.Once);
        }

        [Fact]
        public async Task RefreshTokenAsync_RejectsAnExpiredTokenWithoutRevokingEverything()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetRefreshTokenAsync(It.IsAny<string>()))
                .ReturnsAsync(StoredToken(MemberId, expiresAt: DateTime.UtcNow.AddMinutes(-1)));

            await using var act = NewContext();
            var result = await NewSut(act).RefreshTokenAsync("stale");

            Assert.True(result.IsFailure);
            _redis.Verify(r => r.RevokeAllUserTokensAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task RefreshTokenAsync_RejectsATokenWhoseUserIsGone()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetRefreshTokenAsync(It.IsAny<string>()))
                .ReturnsAsync(StoredToken(Guid.NewGuid()));

            await using var act = NewContext();
            var result = await NewSut(act).RefreshTokenAsync("orphaned");

            Assert.True(result.IsFailure);
            Assert.Equal("Unauthorized", result.Error!.Code);
        }

        [Fact]
        public async Task RefreshTokenAsync_MarksTheOldTokenRevokedAndIssuesANewPair()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetRefreshTokenAsync(It.IsAny<string>()))
                .ReturnsAsync(StoredToken(MemberId, ip: "198.51.100.4"));

            await using var act = NewContext();
            var result = await NewSut(act).RefreshTokenAsync("current");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(NewRefreshToken, result.Value!.RefreshToken);

            _redis.Verify(r => r.MarkRefreshTokenRevokedAsync(TokenHasher.Sha256("current")), Times.Once);
            _redis.Verify(r => r.DeleteRefreshTokenAsync(It.IsAny<string>()), Times.Never);

            _redis.Verify(r => r.SetRefreshTokenAsync(
                TokenHasher.Sha256(NewRefreshToken),
                It.Is<RefreshTokenModel>(m => m.UserId == MemberId && m.CreatedByIp == "198.51.100.4"),
                It.IsAny<TimeSpan>()), Times.Once);
        }

        [Fact]
        public async Task LogoutAsync_DeletesTheCallersOwnToken()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetRefreshTokenAsync(It.IsAny<string>()))
                .ReturnsAsync(StoredToken(MemberId));

            await using var act = NewContext();
            var result = await NewSut(act).LogoutAsync(MemberId, "mine");

            Assert.True(result.IsSuccess);
            _redis.Verify(r => r.DeleteRefreshTokenAsync(TokenHasher.Sha256("mine")), Times.Once);
        }

        /// <summary>
        /// Logging out somebody else's token would let anyone with a captured token end another
        /// person's session, so it is silently ignored rather than refused. Refusing loudly would
        /// also confirm the token exists.
        /// </summary>
        [Fact]
        public async Task LogoutAsync_IgnoresATokenOwnedBySomebodyElse()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetRefreshTokenAsync(It.IsAny<string>()))
                .ReturnsAsync(StoredToken(LeadId));

            await using var act = NewContext();
            var result = await NewSut(act).LogoutAsync(MemberId, "theirs");

            Assert.True(result.IsSuccess);
            _redis.Verify(r => r.DeleteRefreshTokenAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task LogoutAsync_IsHarmlessForATokenThatNoLongerExists()
        {
            await SeedAsync();
            _redis.Setup(r => r.GetRefreshTokenAsync(It.IsAny<string>())).ReturnsAsync((RefreshTokenModel?)null);

            await using var act = NewContext();
            var result = await NewSut(act).LogoutAsync(MemberId, "gone");

            Assert.True(result.IsSuccess);
            _redis.Verify(r => r.DeleteRefreshTokenAsync(TokenHasher.Sha256("gone")), Times.Once);
        }

        [Fact]
        public async Task CheckEmailAsync_AnswersForBothCasesAndNormalisesFirst()
        {
            await SeedAsync();

            await using var act = NewContext();
            var sut = NewSut(act);

            Assert.True((await sut.CheckEmailAsync("  Member@Example.COM ")).Value!.Exists);
            Assert.False((await sut.CheckEmailAsync("nobody@example.com")).Value!.Exists);
        }

        [Fact]
        public async Task SendVerificationCodeAsync_StoresTheHashedCodeAndEmailsThePlaintext()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetVerificationCodeCreatedAtAsync(It.IsAny<string>()))
                .ReturnsAsync((DateTime?)null);

            string? emailedCode = null;
            _email
                .Setup(e => e.SendEmailVerificationCodeAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string, string>((_, _, code) => emailedCode = code)
                .Returns(Task.CompletedTask);

            await using var act = NewContext();
            var result = await NewSut(act).SendVerificationCodeAsync("Newcomer@Example.com");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.NotNull(emailedCode);
            Assert.Equal(6, emailedCode.Length);

            _redis.Verify(r => r.StoreVerificationCodeAsync(
                "newcomer@example.com", $"hashed:{emailedCode}", TimeSpan.FromMinutes(15)), Times.Once);
        }

        [Fact]
        public async Task SendVerificationCodeAsync_RefusesASecondRequestInsideAMinute()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetVerificationCodeCreatedAtAsync(It.IsAny<string>()))
                .ReturnsAsync(DateTime.UtcNow.AddSeconds(-30));

            await using var act = NewContext();
            var result = await NewSut(act).SendVerificationCodeAsync("newcomer@example.com");

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            _redis.Verify(r => r.StoreVerificationCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
        }

        [Fact]
        public async Task SendVerificationCodeAsync_AllowsAnotherRequestOnceTheMinuteHasPassed()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetVerificationCodeCreatedAtAsync(It.IsAny<string>()))
                .ReturnsAsync(DateTime.UtcNow.AddMinutes(-2));

            await using var act = NewContext();
            var result = await NewSut(act).SendVerificationCodeAsync("newcomer@example.com");

            Assert.True(result.IsSuccess, result.Error?.Message);
        }

        /// <summary>
        /// Unlike the welcome mail this one is load bearing. A code the caller never receives
        /// leaves them unable to continue, so the failure surfaces rather than being swallowed.
        /// </summary>
        [Fact]
        public async Task SendVerificationCodeAsync_ReportsAFailedSendAsAnError()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetVerificationCodeCreatedAtAsync(It.IsAny<string>()))
                .ReturnsAsync((DateTime?)null);

            _email
                .Setup(e => e.SendEmailVerificationCodeAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new EmailSendException("provider down"));

            await using var act = NewContext();
            var result = await NewSut(act).SendVerificationCodeAsync("newcomer@example.com");

            Assert.True(result.IsFailure);
            Assert.Equal("Internal", result.Error!.Code);
        }

        [Fact]
        public async Task VerifyEmailCodeAsync_MarksTheCodeUsedWhenItMatches()
        {
            await SeedAsync();
            _redis
                .Setup(r => r.GetVerificationCodeHashAsync("newcomer@example.com"))
                .ReturnsAsync("hashed:123456");

            await using var act = NewContext();
            var result = await NewSut(act).VerifyEmailCodeAsync("Newcomer@Example.com", "123456");

            Assert.True(result.IsSuccess, result.Error?.Message);
            _redis.Verify(r => r.MarkVerificationCodeUsedAsync("newcomer@example.com"), Times.Once);
        }

        [Fact]
        public async Task VerifyEmailCodeAsync_AnswersIdenticallyForAMissingAndAWrongCode()
        {
            await SeedAsync();

            await using var act = NewContext();
            var sut = NewSut(act);

            _redis.Setup(r => r.GetVerificationCodeHashAsync(It.IsAny<string>())).ReturnsAsync((string?)null);
            var missing = await sut.VerifyEmailCodeAsync("newcomer@example.com", "123456");

            _redis.Setup(r => r.GetVerificationCodeHashAsync(It.IsAny<string>())).ReturnsAsync("hashed:999999");
            var wrong = await sut.VerifyEmailCodeAsync("newcomer@example.com", "123456");

            Assert.True(missing.IsFailure);
            Assert.Equal(missing.Error!.Message, wrong.Error!.Message);
            _redis.Verify(r => r.MarkVerificationCodeUsedAsync(It.IsAny<string>()), Times.Never);
        }

        /// <summary>
        /// An unknown address gets the same answer as a known one and writes nothing. Anything
        /// else turns the reset form into a way of discovering who has an account.
        /// </summary>
        [Fact]
        public async Task ForgotPasswordAsync_LooksIdenticalForAnAddressWithNoAccount()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).ForgotPasswordAsync("nobody@example.com", "1.1.1.1");

            Assert.True(result.IsSuccess);

            await using var assert = NewContext();
            Assert.Empty(await assert.PasswordResetTokens.ToListAsync());
            _email.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ForgotPasswordAsync_StoresOnlyTheHashAndEmailsTheLinkWithThePlaintext()
        {
            await SeedAsync();

            string? resetLink = null;
            _email
                .Setup(e => e.SendPasswordResetEmailAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string, string>((_, _, link) => resetLink = link)
                .Returns(Task.CompletedTask);

            await using var act = NewContext();
            await NewSut(act).ForgotPasswordAsync("Member@Example.com", "203.0.113.7");

            Assert.NotNull(resetLink);
            Assert.StartsWith("https://app.plantitask.test/reset-password?token=", resetLink);

            var plaintextToken = resetLink.Split("token=")[1].Split('&')[0];

            await using var assert = NewContext();
            var stored = await assert.PasswordResetTokens.SingleAsync();

            Assert.Equal(MemberId, stored.UserId);
            Assert.Equal(TokenHasher.Sha256(plaintextToken), stored.TokenHash);
            Assert.NotEqual(plaintextToken, stored.TokenHash);
            Assert.Equal("203.0.113.7", stored.IpAddress);
            Assert.False(stored.IsUsed);
        }

        [Fact]
        public async Task ForgotPasswordAsync_StillReportsSuccessWhenTheEmailFails()
        {
            await SeedAsync();

            _email
                .Setup(e => e.SendPasswordResetEmailAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new EmailSendException("provider down"));

            await using var act = NewContext();
            var result = await NewSut(act).ForgotPasswordAsync("member@example.com", "1.1.1.1");

            Assert.True(result.IsSuccess);
        }

        private async Task<string> SeedResetTokenAsync(
            Guid userId, bool used = false, DateTime? expiresAt = null)
        {
            var plaintext = Guid.NewGuid().ToString("N");

            await using var db = NewContext();
            db.PasswordResetTokens.Add(new PasswordResetToken
            {
                UserId = userId,
                TokenHash = TokenHasher.Sha256(plaintext),
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(1),
                IsUsed = used,
                IpAddress = "1.1.1.1"
            });
            await db.SaveChangesAsync();

            return plaintext;
        }

        [Fact]
        public async Task ResetPasswordAsync_SetsTheNewPasswordAndSpendsTheToken()
        {
            await SeedAsync();
            var token = await SeedResetTokenAsync(MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).ResetPasswordAsync(new ResetPasswordDto
            {
                Token = token, Email = "member@example.com", NewPassword = "Br@ndNew1"
            });

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(MemberId, result.Value);
            Assert.Equal("hashed:Br@ndNew1", (await ReadUserAsync(MemberId)).PasswordHash);

            await using var assert = NewContext();
            var stored = await assert.PasswordResetTokens.SingleAsync();
            Assert.True(stored.IsUsed);
            Assert.NotNull(stored.UsedAt);
        }

        /// <summary>
        /// Clicking "forgot password" twice leaves two live links. Redeeming either one has to
        /// kill both, or the older email is still a way into an account whose owner just proved
        /// they had lost control of it.
        /// </summary>
        [Fact]
        public async Task ResetPasswordAsync_SpendsEveryLiveTokenTheUserHolds()
        {
            await SeedAsync();
            var older = await SeedResetTokenAsync(MemberId);
            var newer = await SeedResetTokenAsync(MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).ResetPasswordAsync(new ResetPasswordDto
            {
                Token = newer, Email = "member@example.com", NewPassword = "Br@ndNew1"
            });

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            var stored = await assert.PasswordResetTokens.ToListAsync();
            Assert.Equal(2, stored.Count);
            Assert.All(stored, t => Assert.True(t.IsUsed));
            Assert.All(stored, t => Assert.NotNull(t.UsedAt));

            await using var replay = NewContext();
            var second = await NewSut(replay).ResetPasswordAsync(new ResetPasswordDto
            {
                Token = older, Email = "member@example.com", NewPassword = "Different1!"
            });

            Assert.True(second.IsFailure);
            Assert.Equal("BadRequest", second.Error!.Code);
        }

        /// <summary>
        /// A credential change ends every existing session, because anyone holding a stolen
        /// refresh token would otherwise keep it working across the reset.
        /// </summary>
        [Fact]
        public async Task ResetPasswordAsync_RevokesEverySessionTheUserHad()
        {
            await SeedAsync();
            var token = await SeedResetTokenAsync(MemberId);

            await using var act = NewContext();
            await NewSut(act).ResetPasswordAsync(new ResetPasswordDto
            {
                Token = token, Email = "member@example.com", NewPassword = "Br@ndNew1"
            });

            _redis.Verify(r => r.RevokeAllUserTokensAsync(MemberId), Times.Once);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task ResetPasswordAsync_RefusesASpentOrExpiredToken(bool used, bool expired)
        {
            await SeedAsync();
            var token = await SeedResetTokenAsync(
                MemberId, used: used, expiresAt: expired ? DateTime.UtcNow.AddMinutes(-1) : null);

            await using var act = NewContext();
            var result = await NewSut(act).ResetPasswordAsync(new ResetPasswordDto
            {
                Token = token, Email = "member@example.com", NewPassword = "Br@ndNew1"
            });

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            _redis.Verify(r => r.RevokeAllUserTokensAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task ResetPasswordAsync_RefusesATokenThatWasNeverIssued()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).ResetPasswordAsync(new ResetPasswordDto
            {
                Token = "invented", Email = "member@example.com", NewPassword = "Br@ndNew1"
            });

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        [Fact]
        public async Task ChangePasswordAsync_RequiresTheConfirmationToMatch()
        {
            await SeedAsync();
            await SetPasswordAsync(MemberId, "Old!pass1");

            await using var act = NewContext();
            var result = await NewSut(act).ChangePasswordAsync(MemberId, new ChangePasswordDto
            {
                CurrentPassword = "Old!pass1", NewPassword = "Br@ndNew1", ConfirmNewPassword = "Different1"
            }, "1.1.1.1");

            Assert.True(result.IsFailure);
            Assert.Equal("Validation", result.Error!.Code);
        }

        [Fact]
        public async Task ChangePasswordAsync_RejectsTheWrongCurrentPassword()
        {
            await SeedAsync();
            await SetPasswordAsync(MemberId, "Old!pass1");

            await using var act = NewContext();
            var result = await NewSut(act).ChangePasswordAsync(MemberId, new ChangePasswordDto
            {
                CurrentPassword = "guessing", NewPassword = "Br@ndNew1", ConfirmNewPassword = "Br@ndNew1"
            }, "1.1.1.1");

            Assert.True(result.IsFailure);
            Assert.Equal("Validation", result.Error!.Code);
            Assert.Equal("hashed:Old!pass1", (await ReadUserAsync(MemberId)).PasswordHash);
        }

        [Fact]
        public async Task ChangePasswordAsync_ReturnsNotFoundForAUserWhoDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).ChangePasswordAsync(Guid.NewGuid(), new ChangePasswordDto
            {
                CurrentPassword = "Old!pass1", NewPassword = "Br@ndNew1", ConfirmNewPassword = "Br@ndNew1"
            }, "1.1.1.1");

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        /// <summary>
        /// The revoke has to happen before the new pair is stored. The other order deletes the
        /// token that was just created and signs the caller straight back out.
        /// </summary>
        [Fact]
        public async Task ChangePasswordAsync_RevokesTheOldSessionsBeforeIssuingTheNewPair()
        {
            await SeedAsync();
            await SetPasswordAsync(MemberId, "Old!pass1");

            await using var act = NewContext();
            var result = await NewSut(act).ChangePasswordAsync(MemberId, new ChangePasswordDto
            {
                CurrentPassword = "Old!pass1", NewPassword = "Br@ndNew1", ConfirmNewPassword = "Br@ndNew1"
            }, "1.1.1.1");

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(NewRefreshToken, result.Value!.RefreshToken);
            Assert.Equal("hashed:Br@ndNew1", (await ReadUserAsync(MemberId)).PasswordHash);

            Assert.Equal(new[] { "revoke", "store" }, _callOrder);
        }

        /// <summary>
        /// GoogleJsonWebSignature.ValidateAsync is a static so the happy path needs a real signed
        /// token from Google. The rejection path does not, and it is the one that matters.
        /// </summary>
        [Fact]
        public async Task GoogleLoginAsync_RefusesATokenItCannotValidate()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GoogleLoginAsync(
                new GoogleLoginDto { IdToken = "not-a-real-google-token" }, "1.1.1.1");

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Equal(4, await assert.Users.CountAsync());
        }
    }
}
