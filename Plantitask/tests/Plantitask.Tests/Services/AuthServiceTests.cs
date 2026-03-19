using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Auth;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Models;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestDataBuilder;

namespace Plantitask.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IApplicationDbContext> _mockContext;
    private readonly Mock<IPasswordHasher> _mockHasher;
    private readonly Mock<IJwtTokenGenerator> _mockTokenGen;
    private readonly Mock<IEmailService> _mockEmail;
    private readonly Mock<IRedisService> _mockRedis;
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _mockContext = new Mock<IApplicationDbContext>();
        _mockHasher = new Mock<IPasswordHasher>();
        _mockTokenGen = new Mock<IJwtTokenGenerator>();
        _mockEmail = new Mock<IEmailService>();
        _mockRedis = new Mock<IRedisService>();
        _mockLogger = new Mock<ILogger<AuthService>>();
        _mockConfig = new Mock<IConfiguration>();

        _mockConfig.Setup(c => c["Jwt:RefreshTokenExpirationDays"]).Returns("7");
        _mockConfig.Setup(c => c["App:FrontendUrl"]).Returns("https://localhost:5001");

        var googleSettings = Options.Create(new GoogleAuthSettings { ClientId = "test-client-id" });

        _sut = new AuthService(
            _mockContext.Object, _mockHasher.Object, _mockTokenGen.Object,
            _mockEmail.Object, _mockLogger.Object, _mockConfig.Object,
            _mockRedis.Object, googleSettings);
    }


    [Fact]
    public async Task RegisterAsync_EmailNotVerified_ReturnsFailure()
    {
        _mockRedis.Setup(r => r.IsEmailVerifiedAsync("new@test.com")).ReturnsAsync(false);
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User>()).Object);

        var dto = new RegisterDto { Email = "new@test.com", UserName = "newuser", Password = "P@ss1", FirstName = "N", LastName = "U" };

        var result = await _sut.RegisterAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Equal("BadRequest", result.Error!.Code);
        Assert.Contains("verified", result.Error.Message);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ReturnsConflict()
    {
        var existing = CreateUser(email: "existing@test.com");
        _mockRedis.Setup(r => r.IsEmailVerifiedAsync("existing@test.com")).ReturnsAsync(true);
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { existing }).Object);

        var dto = new RegisterDto { Email = "existing@test.com", UserName = "newuser", Password = "P@ss1", FirstName = "N", LastName = "U" };

        var result = await _sut.RegisterAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("email already exists", result.Error.Message);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateUserName_ReturnsConflict()
    {
        var existing = CreateUser(userName: "takenname", email: "other@test.com");
        _mockRedis.Setup(r => r.IsEmailVerifiedAsync("new@test.com")).ReturnsAsync(true);
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { existing }).Object);

        var dto = new RegisterDto { Email = "new@test.com", UserName = "takenname", Password = "P@ss1", FirstName = "N", LastName = "U" };

        var result = await _sut.RegisterAsync(dto);

        Assert.True(result.IsFailure);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("username already exists", result.Error.Message);
    }

    [Fact]
    public async Task RegisterAsync_ValidData_ReturnsSuccessWithTokens()
    {
        _mockRedis.Setup(r => r.IsEmailVerifiedAsync("new@test.com")).ReturnsAsync(true);
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User>()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockHasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hashed_pw");
        _mockTokenGen.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("access_123");
        _mockTokenGen.Setup(t => t.GenerateRefreshToken()).Returns("refresh_456");

        var dto = new RegisterDto { Email = "new@test.com", UserName = "newuser", Password = "Password123!", FirstName = "New", LastName = "User" };

        var result = await _sut.RegisterAsync(dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("access_123", result.Value!.AccessToken);
        Assert.Equal("refresh_456", result.Value.RefreshToken);
        Assert.Equal("newuser", result.Value.UserName);
        _mockHasher.Verify(h => h.HashPassword("Password123!"), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_SendsWelcomeEmail()
    {
        _mockRedis.Setup(r => r.IsEmailVerifiedAsync("new@test.com")).ReturnsAsync(true);
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User>()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockHasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hash");
        _mockTokenGen.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("t");
        _mockTokenGen.Setup(t => t.GenerateRefreshToken()).Returns("r");

        await _sut.RegisterAsync(new RegisterDto { Email = "new@test.com", UserName = "newuser", Password = "P@ss1", FirstName = "N", LastName = "U" });

        _mockEmail.Verify(e => e.SendWelcomeEmailAsync("new@test.com", "newuser"), Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_WelcomeEmailFailure_DoesNotBlockRegistration()
    {
        _mockRedis.Setup(r => r.IsEmailVerifiedAsync("n@l.com")).ReturnsAsync(true);
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User>()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockHasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hash");
        _mockTokenGen.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("t");
        _mockTokenGen.Setup(t => t.GenerateRefreshToken()).Returns("r");
        _mockEmail.Setup(e => e.SendWelcomeEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("SMTP down"));

        var result = await _sut.RegisterAsync(new RegisterDto { Email = "n@l.com", UserName = "u", Password = "p", FirstName = "F", LastName = "L" });

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.AccessToken);
    }

    // ── Login ──

    [Fact]
    public async Task LoginAsync_NonExistentEmail_ReturnsFailure()
    {
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User>()).Object);

        var result = await _sut.LoginAsync(new LoginDto { Email = "nobody@test.com", Password = "x" });

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsFailure()
    {
        var user = CreateUser(email: "user@test.com");
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { user }).Object);
        _mockHasher.Setup(h => h.VerifyPassword("wrong", user.PasswordHash)).Returns(false);

        var result = await _sut.LoginAsync(new LoginDto { Email = "user@test.com", Password = "wrong" });

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
    }

    [Fact]
    public async Task LoginAsync_EmailNotConfirmed_ReturnsFailure()
    {
        var user = CreateUser(email: "user@test.com");
        user.IsEmailConfirmed = false;
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { user }).Object);
        _mockHasher.Setup(h => h.VerifyPassword("correct", user.PasswordHash)).Returns(true);

        var result = await _sut.LoginAsync(new LoginDto { Email = "user@test.com", Password = "correct" });

        Assert.True(result.IsFailure);
        Assert.Equal("BadRequest", result.Error!.Code);
        Assert.Contains("verify your email", result.Error.Message);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsSuccessAndUpdatesLastLogin()
    {
        var user = CreateUser(email: "user@test.com");
        user.IsEmailConfirmed = true;
        user.LastLoginAt = null;

        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { user }).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockHasher.Setup(h => h.VerifyPassword("correct", user.PasswordHash)).Returns(true);
        _mockTokenGen.Setup(t => t.GenerateAccessToken(user)).Returns("t");
        _mockTokenGen.Setup(t => t.GenerateRefreshToken()).Returns("r");

        var result = await _sut.LoginAsync(new LoginDto { Email = "user@test.com", Password = "correct" });

        Assert.True(result.IsSuccess);
        Assert.NotNull(user.LastLoginAt);
    }


    [Fact]
    public async Task RefreshTokenAsync_InvalidToken_ReturnsFailure()
    {
        _mockRedis.Setup(r => r.GetRefreshTokenAsync("bad")).ReturnsAsync((RefreshTokenModel?)null);

        var result = await _sut.RefreshTokenAsync("bad");

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
    }

    [Fact]
    public async Task RefreshTokenAsync_RevokedToken_ReturnsFailure()
    {
        _mockRedis.Setup(r => r.GetRefreshTokenAsync("revoked")).ReturnsAsync(new RefreshTokenModel
        {
            UserId = UserId1,
            IsRevoked = true,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            CreatedByIp = "127.0.0.1"
        });

        var result = await _sut.RefreshTokenAsync("revoked");

        Assert.True(result.IsFailure);
        Assert.Contains("revoked", result.Error!.Message);
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredToken_ReturnsFailure()
    {
        _mockRedis.Setup(r => r.GetRefreshTokenAsync("expired")).ReturnsAsync(new RefreshTokenModel
        {
            UserId = UserId1,
            IsRevoked = false,
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            CreatedByIp = "127.0.0.1"
        });

        var result = await _sut.RefreshTokenAsync("expired");

        Assert.True(result.IsFailure);
        Assert.Contains("expired", result.Error!.Message);
    }


    [Fact]
    public async Task ForgotPasswordAsync_ValidEmail_ReturnsSuccessAndSendsEmail()
    {
        var user = CreateUser(email: "user@testing.com", userName: "testuser");
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { user }).Object);
        var tokens = new List<PasswordResetToken>();
        _mockContext.Setup(c => c.PasswordResetTokens).Returns(MockDbSetFactory.Create(tokens).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockHasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hashed_token");

        var result = await _sut.ForgotPasswordAsync("user@testing.com");

        Assert.True(result.IsSuccess);
        Assert.Single(tokens);
        Assert.Equal(user.Id, tokens[0].UserId);
        Assert.False(tokens[0].IsUsed);
        _mockEmail.Verify(e => e.SendPasswordResetEmailAsync("user@testing.com", "testuser",
            It.Is<string>(link => link.Contains("reset-password") && link.Contains("token="))), Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_NonExistentEmail_ReturnsSuccessWithoutRevealingInfo()
    {
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User>()).Object);

        var result = await _sut.ForgotPasswordAsync("nobody@test.com");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ForgotPasswordAsync_EmailServiceFails_DoesNotThrow()
    {
        var user = CreateUser(email: "user@test.com");
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { user }).Object);
        _mockContext.Setup(c => c.PasswordResetTokens).Returns(MockDbSetFactory.Create(new List<PasswordResetToken>()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockHasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("h");
        _mockEmail.Setup(e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("SMTP down"));

        var result = await _sut.ForgotPasswordAsync("user@test.com");

        Assert.True(result.IsSuccess);
    }


    [Fact]
    public async Task LogoutAsync_RevokesRefreshToken()
    {
        var result = await _sut.LogoutAsync("some_token");

        Assert.True(result.IsSuccess);
        _mockRedis.Verify(r => r.RevokeRefreshTokenAsync("some_token"), Times.Once);
    }


    [Fact]
    public async Task SendVerificationCodeAsync_TooSoon_ReturnsFailure()
    {
        _mockRedis.Setup(r => r.GetVerificationCodeCreatedAtAsync("test@test.com"))
            .ReturnsAsync(DateTime.UtcNow.AddSeconds(-30));

        var result = await _sut.SendVerificationCodeAsync("test@test.com");

        Assert.True(result.IsFailure);
        Assert.Equal("BadRequest", result.Error!.Code);
        Assert.Contains("wait", result.Error.Message);
    }

    [Fact]
    public async Task VerifyEmailCodeAsync_InvalidCode_ReturnsFailure()
    {
        _mockRedis.Setup(r => r.GetVerificationCodeHashAsync("test@test.com")).ReturnsAsync("hashed_code");
        _mockHasher.Setup(h => h.VerifyPassword("wrong", "hashed_code")).Returns(false);

        var result = await _sut.VerifyEmailCodeAsync("test@test.com", "wrong");

        Assert.True(result.IsFailure);
        Assert.Equal("BadRequest", result.Error!.Code);
    }

    [Fact]
    public async Task VerifyEmailCodeAsync_ExpiredCode_ReturnsFailure()
    {
        _mockRedis.Setup(r => r.GetVerificationCodeHashAsync("test@test.com")).ReturnsAsync((string?)null);

        var result = await _sut.VerifyEmailCodeAsync("test@test.com", "123456");

        Assert.True(result.IsFailure);
        Assert.Equal("BadRequest", result.Error!.Code);
    }
}
