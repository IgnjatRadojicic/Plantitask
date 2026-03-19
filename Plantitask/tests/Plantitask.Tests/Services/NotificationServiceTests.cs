using Microsoft.Extensions.Logging;
using Moq;
using Plantitask.Core.DTO.Comments;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestDataBuilder;

namespace Plantitask.Tests.Services;

public class NotificationServiceTests
{
    private readonly Mock<IApplicationDbContext> _mockContext;
    private readonly Mock<ILogger<NotificationService>> _mockLogger;
    private readonly Mock<IEmailService> _mockEmail;
    private readonly NotificationService _sut;

    public NotificationServiceTests()
    {
        _mockContext = new Mock<IApplicationDbContext>();
        _mockLogger = new Mock<ILogger<NotificationService>>();
        _mockEmail = new Mock<IEmailService>();

        _sut = new NotificationService(_mockContext.Object, _mockLogger.Object, _mockEmail.Object);
    }


    [Fact]
    public async Task NotifyTaskCreatedAsync_NoAssignee_ReturnsNull()
    {
        var task = new TaskDto { Id = TaskId1, Title = "Test", AssignedToId = null, CreatedBy = UserId1 };

        var result = await _sut.NotifyTaskCreatedAsync(UserId1, task);

        Assert.Null(result);
    }

    [Fact]
    public async Task NotifyTaskCreatedAsync_AssignedToSelf_ReturnsNull()
    {
        var task = new TaskDto { Id = TaskId1, Title = "Test", AssignedToId = UserId1, CreatedBy = UserId1 };

        var result = await _sut.NotifyTaskCreatedAsync(UserId1, task);

        Assert.Null(result);
    }

    [Fact]
    public async Task NotifyTaskCreatedAsync_NotificationsDisabled_ReturnsNull()
    {
        var task = new TaskDto { Id = TaskId1, Title = "Test", AssignedToId = UserId2, CreatedBy = UserId1 };

        var prefs = new List<NotificationPreference>
        {
            CreatePreference(UserId2, NotificationType.TaskAssigned, isEnabled: false)
        };
        _mockContext.Setup(c => c.NotificationPreferences).Returns(MockDbSetFactory.Create(prefs).Object);

        var result = await _sut.NotifyTaskCreatedAsync(UserId1, task);

        Assert.Null(result);
    }

    [Fact]
    public async Task NotifyTaskCreatedAsync_ValidAssignee_CreatesNotification()
    {
        var task = new TaskDto { Id = TaskId1, Title = "Test Task", AssignedToId = UserId2, CreatedBy = UserId1 };

        _mockContext.Setup(c => c.NotificationPreferences)
            .Returns(MockDbSetFactory.Create(new List<NotificationPreference>()).Object);
        _mockContext.Setup(c => c.Notifications)
            .Returns(MockDbSetFactory.Create(new List<Notification>()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _sut.NotifyTaskCreatedAsync(UserId1, task);

        Assert.NotNull(result);
        Assert.Equal(UserId2, result!.UserId);
        Assert.Equal(NotificationType.TaskAssigned, result.Type);
        Assert.Contains("Test Task", result.Message);
    }


    [Fact]
    public async Task NotifyTaskCommentAddedAsync_NotifiesCreatorAndAssignee()
    {
        var task = new TaskDto
        {
            Id = TaskId1,
            Title = "Test Task",
            GroupId = GroupId1,
            CreatedBy = UserId1,
            AssignedToId = UserId2
        };
        var comment = new CommentDto
        {
            Id = Guid.NewGuid(),
            UserId = UserId3,
            UserName = "commenter",
            Content = "Nice work"
        };

        _mockContext.Setup(c => c.NotificationPreferences)
            .Returns(MockDbSetFactory.Create(new List<NotificationPreference>()).Object);
        _mockContext.Setup(c => c.Notifications)
            .Returns(MockDbSetFactory.Create(new List<Notification>()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var results = await _sut.NotifyTaskCommentAddedAsync(GroupId1, task, comment);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, n => n.UserId == UserId1);
        Assert.Contains(results, n => n.UserId == UserId2);
    }

    [Fact]
    public async Task NotifyTaskCommentAddedAsync_CommentByCreator_OnlyNotifiesAssignee()
    {
        var task = new TaskDto
        {
            Id = TaskId1,
            Title = "Test Task",
            GroupId = GroupId1,
            CreatedBy = UserId1,
            AssignedToId = UserId2
        };
        var comment = new CommentDto
        {
            Id = Guid.NewGuid(),
            UserId = UserId1,
            UserName = "creator",
            Content = "Update"
        };

        _mockContext.Setup(c => c.NotificationPreferences)
            .Returns(MockDbSetFactory.Create(new List<NotificationPreference>()).Object);
        _mockContext.Setup(c => c.Notifications)
            .Returns(MockDbSetFactory.Create(new List<Notification>()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var results = await _sut.NotifyTaskCommentAddedAsync(GroupId1, task, comment);

        Assert.Single(results);
        Assert.Equal(UserId2, results[0].UserId);
    }


    [Fact]
    public async Task GetUserNotificationsAsync_ReturnsUserNotifications()
    {
        var notifications = new List<Notification>
        {
            CreateNotification(UserId1),
            CreateNotification(UserId1),
            CreateNotification(UserId2),
        };
        _mockContext.Setup(c => c.Notifications).Returns(MockDbSetFactory.Create(notifications).Object);

        var result = await _sut.GetUserNotificationsAsync(UserId1);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task GetUserNotificationsAsync_UnreadOnly_FiltersCorrectly()
    {
        var notifications = new List<Notification>
        {
            CreateNotification(UserId1, isRead: false),
            CreateNotification(UserId1, isRead: true),
            CreateNotification(UserId1, isRead: false),
        };
        _mockContext.Setup(c => c.Notifications).Returns(MockDbSetFactory.Create(notifications).Object);

        var result = await _sut.GetUserNotificationsAsync(UserId1, unreadOnly: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task GetUserNotificationsAsync_Pagination_ReturnsCorrectPage()
    {
        var notifications = new List<Notification>();
        for (int i = 0; i < 25; i++)
        {
            var n = CreateNotification(UserId1);
            n.CreatedAt = DateTime.UtcNow.AddMinutes(-i);
            notifications.Add(n);
        }
        _mockContext.Setup(c => c.Notifications).Returns(MockDbSetFactory.Create(notifications).Object);

        var result = await _sut.GetUserNotificationsAsync(UserId1, pageNumber: 2, pageSize: 10);

        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value!.Items.Count);
        Assert.Equal(25, result.Value.TotalCount);
        Assert.Equal(3, result.Value.TotalPages);
        Assert.True(result.Value.HasPreviousPage);
        Assert.True(result.Value.HasNextPage);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsCorrectCount()
    {
        var notifications = new List<Notification>
        {
            CreateNotification(UserId1, isRead: false),
            CreateNotification(UserId1, isRead: false),
            CreateNotification(UserId1, isRead: true),
        };
        _mockContext.Setup(c => c.Notifications).Returns(MockDbSetFactory.Create(notifications).Object);

        var result = await _sut.GetUnreadCountAsync(UserId1);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
    }


    [Fact]
    public async Task DeleteNotificationAsync_NotFound_ReturnsNotFound()
    {
        _mockContext.Setup(c => c.Notifications).Returns(MockDbSetFactory.Create(new List<Notification>()).Object);

        var result = await _sut.DeleteNotificationAsync(Guid.NewGuid(), UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task DeleteNotificationAsync_ValidNotification_SoftDeletes()
    {
        var notification = CreateNotification(UserId1);
        _mockContext.Setup(c => c.Notifications).Returns(MockDbSetFactory.Create(new List<Notification> { notification }).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _sut.DeleteNotificationAsync(notification.Id, UserId1);

        Assert.True(result.IsSuccess);
        Assert.True(notification.IsDeleted);
        Assert.NotNull(notification.DeletedAt);
    }

    [Fact]
    public async Task DeleteNotificationAsync_WrongUser_ReturnsNotFound()
    {
        var notification = CreateNotification(UserId1);
        _mockContext.Setup(c => c.Notifications).Returns(MockDbSetFactory.Create(new List<Notification> { notification }).Object);

        var result = await _sut.DeleteNotificationAsync(notification.Id, UserId2);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    // ── Preferences ──

    [Fact]
    public async Task GetUserPreferencesAsync_ReturnsAllTypesWithDefaults()
    {
        _mockContext.Setup(c => c.NotificationPreferences)
            .Returns(MockDbSetFactory.Create(new List<NotificationPreference>()).Object);

        var result = await _sut.GetUserPreferencesAsync(UserId1);

        Assert.True(result.IsSuccess);
        var allTypes = Enum.GetValues<NotificationType>();
        Assert.Equal(allTypes.Length, result.Value!.Count);
        Assert.All(result.Value, p => Assert.True(p.IsEnabled));
    }

    [Fact]
    public async Task GetUserPreferencesAsync_RespectsExistingPreferences()
    {
        var prefs = new List<NotificationPreference>
        {
            CreatePreference(UserId1, NotificationType.TaskAssigned, isEnabled: false)
        };
        _mockContext.Setup(c => c.NotificationPreferences).Returns(MockDbSetFactory.Create(prefs).Object);

        var result = await _sut.GetUserPreferencesAsync(UserId1);

        Assert.True(result.IsSuccess);
        var taskAssigned = result.Value!.First(p => p.Type == NotificationType.TaskAssigned);
        Assert.False(taskAssigned.IsEnabled);
    }

    [Fact]
    public async Task ShouldNotifyAsync_NoPreference_ReturnsTrue()
    {
        _mockContext.Setup(c => c.NotificationPreferences)
            .Returns(MockDbSetFactory.Create(new List<NotificationPreference>()).Object);

        var result = await _sut.ShouldNotifyAsync(UserId1, NotificationType.TaskAssigned);

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldNotifyAsync_DisabledPreference_ReturnsFalse()
    {
        var prefs = new List<NotificationPreference>
        {
            CreatePreference(UserId1, NotificationType.TaskAssigned, isEnabled: false)
        };
        _mockContext.Setup(c => c.NotificationPreferences).Returns(MockDbSetFactory.Create(prefs).Object);

        var result = await _sut.ShouldNotifyAsync(UserId1, NotificationType.TaskAssigned);

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldEmailAsync_DisabledEmail_ReturnsFalse()
    {
        var prefs = new List<NotificationPreference>
        {
            CreatePreference(UserId1, NotificationType.TaskAssigned, isEnabled: true, isEmailEnabled: false)
        };
        _mockContext.Setup(c => c.NotificationPreferences).Returns(MockDbSetFactory.Create(prefs).Object);

        var result = await _sut.ShouldEmailAsync(UserId1, NotificationType.TaskAssigned);

        Assert.False(result);
    }

    // ── TrySendTaskAssignmentEmailAsync ──

    [Fact]
    public async Task TrySendTaskAssignmentEmailAsync_EmailDisabled_DoesNotSend()
    {
        var prefs = new List<NotificationPreference>
        {
            CreatePreference(UserId2, NotificationType.TaskAssigned, isEmailEnabled: false)
        };
        _mockContext.Setup(c => c.NotificationPreferences).Returns(MockDbSetFactory.Create(prefs).Object);

        await _sut.TrySendTaskAssignmentEmailAsync(UserId2, "Task", "Group", "Assigner");

        _mockEmail.Verify(e => e.SendTaskAssignmentEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TrySendTaskAssignmentEmailAsync_EmailServiceFails_DoesNotThrow()
    {
        _mockContext.Setup(c => c.NotificationPreferences)
            .Returns(MockDbSetFactory.Create(new List<NotificationPreference>()).Object);

        var users = new List<User> { CreateUser(id: UserId2, email: "user@test.com", userName: "user2") };
        _mockContext.Setup(c => c.Users).Returns(MockDbSetFactory.Create(users).Object);

        _mockEmail.Setup(e => e.SendTaskAssignmentEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("SMTP down"));

        await _sut.TrySendTaskAssignmentEmailAsync(UserId2, "Task", "Group", "Assigner");

    }


    [Fact]
    public async Task NotifyTaskUpdatedAsync_NoAssignee_ReturnsNull()
    {
        var task = new TaskDto { Id = TaskId1, AssignedToId = null, CreatedBy = UserId1 };

        var result = await _sut.NotifyTaskUpdatedAsync(GroupId1, task);

        Assert.Null(result);
    }

    [Fact]
    public async Task NotifyTaskUpdatedAsync_AssignedToSelf_ReturnsNull()
    {
        var task = new TaskDto { Id = TaskId1, AssignedToId = UserId1, CreatedBy = UserId1 };

        var result = await _sut.NotifyTaskUpdatedAsync(GroupId1, task);

        Assert.Null(result);
    }
}
