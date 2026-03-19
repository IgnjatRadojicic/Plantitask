using Plantitask.Core.Constants;
using Plantitask.Core.Entities;
using Plantitask.Core.Entities.Lookups;
using Plantitask.Core.Enums;

namespace Plantitask.Tests.Helpers;

public static class TestDataBuilder
{

    public static readonly Guid UserId1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid UserId2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public static readonly Guid UserId3 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    public static readonly Guid GroupId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid GroupId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid TaskId1 = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    public static readonly Guid TaskId2 = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    public static class RoleIds
    {
        public const int Owner = 1;
        public const int Manager = 2;
        public const int TeamLead = 3;
        public const int Member = 4;
    }



    public static List<TaskStatusLookup> CreateStatuses() => new()
    {
        new TaskStatusLookup { Id = (int)TaskStatusItem.NotStarted, Name = "NotStarted", DisplayName = "Not Started", Description = "Task has not been started yet", Color = "#6c757d", DisplayOrder = 1, IsActive = true },
        new TaskStatusLookup { Id = (int)TaskStatusItem.InProgress, Name = "InProgress", DisplayName = "In Progress", Description = "Task is currently being worked on", Color = "#0dcaf0", DisplayOrder = 2, IsActive = true },
        new TaskStatusLookup { Id = (int)TaskStatusItem.UnderReview, Name = "UnderReview", DisplayName = "Under Review", Description = "Task is under review", Color = "#ffc107", DisplayOrder = 3, IsActive = true },
        new TaskStatusLookup { Id = (int)TaskStatusItem.Completed, Name = "Completed", DisplayName = "Completed", Description = "Task is completed", Color = "#198754", DisplayOrder = 4, IsActive = true },
    };

    public static List<TaskPriorityLookup> CreatePriorities() => new()
    {
        new TaskPriorityLookup { Id = (int)TaskPriority.Low, Name = "Low", Description = "Low priority task", Color = "#6c757d", DisplayOrder = 1, IsActive = true },
        new TaskPriorityLookup { Id = (int)TaskPriority.Medium, Name = "Medium", Description = "Medium priority task", Color = "#0dcaf0", DisplayOrder = 2, IsActive = true },
        new TaskPriorityLookup { Id = (int)TaskPriority.High, Name = "High", Description = "High priority task", Color = "#ffc107", DisplayOrder = 3, IsActive = true },
        new TaskPriorityLookup { Id = (int)TaskPriority.Urgent, Name = "Urgent", Description = "Urgent priority task", Color = "#dc3545", DisplayOrder = 4, IsActive = true },
    };

    public static List<GroupRoleLookup> CreateRoles() => new()
    {
        new GroupRoleLookup { Id = RoleIds.Owner, Name = "Owner", DisplayName = "Owner", Description = "Full control over the group", PermissionLevel = PermissionLevels.Owner, IsActive = true },
        new GroupRoleLookup { Id = RoleIds.Manager, Name = "Manager", DisplayName = "Manager", Description = "Can manage members and tasks", PermissionLevel = PermissionLevels.Manager, IsActive = true },
        new GroupRoleLookup { Id = RoleIds.TeamLead, Name = "TeamLead", DisplayName = "Team Lead", Description = "Can manage tasks", PermissionLevel = PermissionLevels.TeamLead, IsActive = true },
        new GroupRoleLookup { Id = RoleIds.Member, Name = "Member", DisplayName = "Member", Description = "Can view and work on tasks", PermissionLevel = PermissionLevels.Member, IsActive = true },
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
        Guid userId, Guid groupId, int roleId = RoleIds.TeamLead,
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
        int statusId = (int)TaskStatusItem.NotStarted,
        int priorityId = (int)TaskPriority.Medium,
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
            UserEmail = "test@example.com",
            CreatedAt = DateTime.UtcNow
        };
}