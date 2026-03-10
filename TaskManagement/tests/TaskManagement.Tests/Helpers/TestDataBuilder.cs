using TaskManagement.Core.Entities;
using TaskManagement.Core.Entities.Lookups;
using TaskManagement.Core.Enums;

namespace TaskManagement.Tests.Helpers;

public static class TestDataBuilder
{
    public static readonly Guid UserId1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid UserId2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid UserId3 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public static readonly Guid GroupId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid GroupId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid TaskId1 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    public static readonly Guid TaskId2 = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");


    public static List<TaskStatusLookup> CreateStatuses() => new()
    {
        new TaskStatusLookup { Id = 1, Name = "NotStarted", DisplayName = "Not Started", Color = "#6B7280", DisplayOrder = 1, IsActive = true },
        new TaskStatusLookup { Id = 2, Name = "InProgress", DisplayName = "In Progress", Color = "#3B82F6", DisplayOrder = 2, IsActive = true },
        new TaskStatusLookup { Id = 3, Name = "UnderReview", DisplayName = "Under Review", Color = "#F59E0B", DisplayOrder = 3, IsActive = true },
        new TaskStatusLookup { Id = 4, Name = "Completed", DisplayName = "Completed", Color = "#10B981", DisplayOrder = 4, IsActive = true },
    };

    public static List<TaskPriorityLookup> CreatePriorities() => new()
    {
        new TaskPriorityLookup { Id = 1, Name = "Low", Color = "#6B7280", IsActive = true },
        new TaskPriorityLookup { Id = 2, Name = "Medium", Color = "#F59E0B", IsActive = true },
        new TaskPriorityLookup { Id = 3, Name = "High", Color = "#EF4444", IsActive = true },
        new TaskPriorityLookup { Id = 4, Name = "Critical", Color = "#DC2626", IsActive = true },
    };

    public static List<GroupRoleLookup> CreateRoles() => new()
    {
        new GroupRoleLookup { Id = 1, Name = "Member", DisplayName = "Member", PermissionLevel = 25 },
        new GroupRoleLookup { Id = 2, Name = "TeamLead", DisplayName = "Team Lead", PermissionLevel = 50 },
        new GroupRoleLookup { Id = 3, Name = "Manager", DisplayName = "Manager", PermissionLevel = 75 },
        new GroupRoleLookup { Id = 4, Name = "Owner", DisplayName = "Owner", PermissionLevel = 100 },
    };



    public static User CreateUser(Guid? id = null, string? userName = null, string? email = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        UserName = userName ?? "testuser",
        Email = email ?? "test@example.com",
        PasswordHash = "hashed_password",
        FirstName = "Test",
        LastName = "User",
        IsEmailConfirmed = false,
        CreatedAt = DateTime.UtcNow
    };

    public static Group CreateGroup(Guid? id = null, string? name = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name ?? "Test Group",
        CreatedAt = DateTime.UtcNow
    };

    public static GroupMember CreateMembership(
        Guid userId, Guid groupId, int roleId = 2,
        GroupRoleLookup? role = null) => new()
        {
            UserId = userId,
            GroupId = groupId,
            RoleId = roleId,
            Role = role ?? CreateRoles().First(r => r.Id == roleId),
            JoinedAt = DateTime.UtcNow
        };

    public static TaskItem CreateTask(
        Guid? id = null,
        Guid? groupId = null,
        Guid? assignedToId = null,
        Guid? createdBy = null,
        int statusId = 1,
        int priorityId = 2,
        DateTime? dueDate = null,
        DateTime? completedAt = null,
        int displayOrder = 0,
        string title = "Test Task")
    {
        var statuses = CreateStatuses();
        var priorities = CreatePriorities();
        var gId = groupId ?? GroupId1;
        var creator = createdBy ?? UserId1;

        return new TaskItem
        {
            Id = id ?? Guid.NewGuid(),
            Title = title,
            Description = "Test description",
            GroupId = gId,
            Group = CreateGroup(gId),
            StatusId = statusId,
            Status = statuses.First(s => s.Id == statusId),
            PriorityId = priorityId,
            Priority = priorities.First(p => p.Id == priorityId),
            AssignedToId = assignedToId,
            AssignedTo = assignedToId.HasValue ? CreateUser(assignedToId) : null,
            CreatedBy = creator,
            Creator = CreateUser(creator),
            DueDate = dueDate,
            CompletedAt = completedAt,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow,
            Attachments = new List<TaskAttachment>(),
            Comments = new List<TaskComment>()
        };
    }

    public static Notification CreateNotification(
        Guid userId,
        NotificationType type = NotificationType.TaskAssigned,
        bool isRead = false) => new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = "Test Notification",
            Message = "Test message",
            RelatedEntityType = "Task",
            IsRead = isRead,
            ReadAt = isRead ? DateTime.UtcNow : null,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow
        };

    public static NotificationPreference CreatePreference(
        Guid userId,
        NotificationType type,
        bool isEnabled = true,
        bool isEmailEnabled = true,
        int? reminderHours = null) => new()
        {
            UserId = userId,
            Type = type,
            IsEnabled = isEnabled,
            IsEmailEnabled = isEmailEnabled,
            ReminderHoursBefore = reminderHours,
            CreatedBy = userId
        };

    public static AuditLog CreateAuditLog(
        Guid? groupId = null,
        string action = "Created",
        string entityType = "Task") => new()
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            Action = action,
            EntityType = entityType,
            UserName = "testuser",
            CreatedAt = DateTime.UtcNow
        };
}