using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using TaskManagement.Core.DTO.Auth;
using TaskManagement.Core.Entities;
using TaskManagement.Core.Interfaces;
using TaskManagement.Core.Models;
using TaskManagement.Infrastructure.Services;
using TaskManagement.Tests.Helpers;
using static TaskManagement.Tests.Helpers.TestDataBuilder;

namespace TaskManagement.Tests.Services;

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

        _sut = new AuthService(
            _mockContext.Object, _mockHasher.Object, _mockTokenGen.Object,
            _mockEmail.Object, _mockLogger.Object, _mockConfig.Object, _mockRedis.Object);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateEmail_ThrowsInvalidOperaiton()
    {
        var existing = CreateUser(email: "existing@test.com");
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { existing }).Object);

        var dto = new RegisterDto { Email = "existing@test.com", UserName = "newuser", Password = "P@ss1", FirstName = "N", LastName = "U" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RegisterAsync(dto));
        Assert.Contains("email already exists", ex.Message);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateUserName_ThrowsInvalidOperation()
    {
        var existing = CreateUser(userName: "takenname", email: "other@test.com");
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { existing }).Object);

        var dto = new RegisterDto { Email = "new@test.com", UserName = "takenname", Password = "P@ss1", FirstName = "N", LastName = "U" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RegisterAsync(dto));
        Assert.Contains("username already exists", ex.Message);
    }

    [Fact]
    public async Task RegisterAsync_ValidData_CreatesUserAndReturnsTokens()
    {
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User>()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockHasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hashed_pw");
        _mockTokenGen.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("access_123");
        _mockTokenGen.Setup(t => t.GenerateRefreshToken()).Returns("refresh_456");

        var dto = new RegisterDto { Email = "new@test.com", UserName = "newuser", Password = "Password123!", FirstName = "New", LastName = "User" };

        var result = await _sut.RegisterAsync(dto);

        Assert.Equal("access_123", result.AccessToken);
        Assert.Equal("refresh_456", result.RefreshToken);
        Assert.Equal("newuser", result.UserName);
        _mockHasher.Verify(h => h.HashPassword("Password123!"), Times.Once);

    }

    [Fact]
    public async Task RegisterAsync_SendWelcome()
    {
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
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User>()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockHasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hash");
        _mockTokenGen.Setup(t => t.GenerateAccessToken(It.IsAny<User>())).Returns("t");
        _mockTokenGen.Setup(t => t.GenerateRefreshToken()).Returns("r");
        _mockEmail.Setup(e => e.SendWelcomeEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("SMPT down"));

        var result = await _sut.RegisterAsync(new RegisterDto { Email = "n@l.com", UserName = "u", Password = "p", FirstName = "F", LastName = "L" });

        Assert.NotNull(result.AccessToken);


    }

    [Fact]
    public async Task LoginAsync_NonExistentEmail_ThrowsUnauthorized()
    {
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User>()).Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.LoginAsync(new LoginDto { Email = "nobody@test.com", Password = "x" }));
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsUnauthorized()
    {
        var user = CreateUser(email: "user@test.com");
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { user }).Object);
        _mockHasher.Setup(h => h.VerifyPassword("wrong", user.PasswordHash)).Returns(false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.LoginAsync(new LoginDto { Email = "user@test.com", Password = "wrong" }));
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_UpdatesLastLoginAt()
    {
        var user = CreateUser(email: "user@test.com");
        user.LastLoginAt = null;

        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { user }).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockHasher.Setup(h => h.VerifyPassword("correct", user.PasswordHash)).Returns(true);
        _mockTokenGen.Setup(t => t.GenerateAccessToken(user)).Returns("t");
        _mockTokenGen.Setup(t => t.GenerateRefreshToken()).Returns("r");

        await _sut.LoginAsync(new LoginDto { Email = "user@test.com", Password = "correct" });

        Assert.NotNull(user.LastLoginAt);
    }

    [Fact]
    public async Task RefreshTokenAsync_InvalidToken_ThrowUnauthorized()
    {
        _mockRedis.Setup(r => r.GetRefreshTokenAsync("bad")).ReturnsAsync((RefreshTokenModel?)null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.RefreshTokenAsync("bad"));
    }

    [Fact]
    public async Task RefreshTokenAsync_RevokedToken_ThrowsUnauthorized()
    {
        _mockRedis.Setup(r => r.GetRefreshTokenAsync("revoked")).ReturnsAsync(new RefreshTokenModel
        {
            UserId = UserId1,
            IsRevoked = true,
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            CreatedByIp = "127.0.0.1"
        });

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.RefreshTokenAsync("revoked"));
        Assert.Contains("revoked", ex.Message);
    }

    [Fact]
    public async Task RefreshTokenAsync_ExpiredToken_ThrowsUnauthorized()
    {
        _mockRedis.Setup(r => r.GetRefreshTokenAsync("expired")).ReturnsAsync(new RefreshTokenModel
        {
            UserId = UserId1,
            IsRevoked = false,
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            CreatedByIp = "127.0.0.1"
        });

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _sut.RefreshTokenAsync("expired"));
        Assert.Contains("expired", ex.Message);
    }

    [Fact]

    public async Task ForgotPasswordAsync_ValidEmail_CreatesTokenAndSendsEmail()
    {
        var user = CreateUser(email: "user@testing.com", userName: "testuser");
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(new List<User> { user }).Object);
        var tokens = new List<PasswordResetToken>();
        _mockContext.Setup(c => c.PasswordResetTokens).Returns(MockDbSetFactory.Create(tokens).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _mockHasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns("hashed_token");

        await _sut.ForgotPasswordAsync("user@testing.com");

        Assert.Single(tokens);
        Assert.Equal(user.Id, tokens[0].UserId);
        Assert.False(tokens[0].IsUsed);
        _mockEmail.Verify(e => e.SendPasswordResetEmailAsync("user@testing.com", "testuser",
            It.Is<string>(link => link.Contains("reset-password") && link.Contains("token="))), Times.Once);
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

        await _sut.ForgotPasswordAsync("user@test.com");
    }

    [Fact]
    public async Task LogoutAsync_RevokesRefreshToken()
    {
        await _sut.LogoutAsync("some_token");
        _mockRedis.Verify(r => r.RevokeRefreshTokenAsync("some_token"), Times.Once);
    }

}

