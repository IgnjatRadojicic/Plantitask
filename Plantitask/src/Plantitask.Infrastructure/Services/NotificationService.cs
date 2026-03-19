using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Comments;
using Plantitask.Core.DTO.Notifications;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<NotificationService> _logger;
    private readonly IEmailService _emailService;

    public NotificationService(
        IApplicationDbContext context,
        ILogger<NotificationService> logger,
        IEmailService emailService)
    {
        _emailService = emailService;
        _context = context;
        _logger = logger;
    }

    public async Task<NotificationDto?> NotifyTaskCreatedAsync(Guid createdByUserId, TaskDto task)
    {
        if (!task.AssignedToId.HasValue)
            return null;
        if (task.AssignedToId.Value == createdByUserId)
            return null;

        if (!await ShouldNotifyAsync(task.AssignedToId.Value, NotificationType.TaskAssigned))
        {
            _logger.LogInformation("User {UserId} has disabled TaskAssigned notifications", task.AssignedToId.Value);
            return null;
        }

        var notification = new Notification
        {
            UserId = task.AssignedToId.Value,
            Type = NotificationType.TaskAssigned,
            Title = "Task Assigned",
            Message = $"You have been assigned to task: {task.Title}",
            RelatedEntityId = task.Id,
            RelatedEntityType = "Task",
            CreatedBy = task.AssignedToId.Value
        };

        return await CreateNotificationAsync(notification);
    }

    public async Task<NotificationDto?> NotifyTaskAssignedAsync(Guid userId, TaskDto task)
    {
        if (!await ShouldNotifyAsync(userId, NotificationType.TaskAssigned))
        {
            _logger.LogInformation("User {UserId} has disabled TaskAssigned notifications", userId);
            return null;
        }

        var notification = new Notification
        {
            UserId = userId,
            Type = NotificationType.TaskAssigned,
            Title = "Task Assigned",
            Message = $"You have been assigned to task: {task.Title}",
            RelatedEntityId = task.Id,
            RelatedEntityType = "Task",
            CreatedBy = userId
        };

        return await CreateNotificationAsync(notification);
    }

    public async Task<List<NotificationDto>> NotifyTaskStatusChangedAsync(Guid groupId, TaskDto task, string oldStatus, string newStatus)
    {
        var members = await _context.GroupMembers
            .Where(gm => gm.GroupId == groupId && gm.UserId != task.CreatedBy)
            .Select(gm => gm.UserId)
            .ToListAsync();

        var disabledMembers = await _context.NotificationPreferences
            .Where(np => members.Contains(np.UserId)
                && np.Type == NotificationType.TaskStatusChanged
                && !np.IsEnabled)
            .Select(np => np.UserId)
            .ToListAsync();

        var createdNotifications = new List<NotificationDto>();

        foreach (var memberId in members)
        {
            if (disabledMembers.Contains(memberId))
            {
                _logger.LogInformation("User {UserId} has disabled TaskStatusChanged notifications", memberId);
                continue;
            }

            var notification = new Notification
            {
                UserId = memberId,
                Type = NotificationType.TaskStatusChanged,
                Title = "Task Status Changed",
                Message = $"Task '{task.Title}' status changed from {oldStatus} to {newStatus}",
                RelatedEntityId = task.Id,
                RelatedEntityType = "Task",
                CreatedBy = memberId
            };

            var dto = await CreateNotificationAsync(notification);
            createdNotifications.Add(dto);
        }

        return createdNotifications;
    }

    public async Task<List<NotificationDto>> NotifyTaskCommentAddedAsync(Guid groupId, TaskDto task, CommentDto comment)
    {
        var usersToNotify = new List<Guid>();

        if (task.CreatedBy != comment.UserId)
            usersToNotify.Add(task.CreatedBy);

        if (task.AssignedToId.HasValue
            && task.AssignedToId.Value != comment.UserId
            && task.AssignedToId.Value != task.CreatedBy)
        {
            usersToNotify.Add(task.AssignedToId.Value);
        }

        var createdNotifications = new List<NotificationDto>();

        foreach (var userId in usersToNotify)
        {
            if (!await ShouldNotifyAsync(userId, NotificationType.TaskCommentAdded))
            {
                _logger.LogInformation("User {UserId} has disabled TaskCommentAdded notifications", userId);
                continue;
            }

            var notification = new Notification
            {
                UserId = userId,
                Type = NotificationType.TaskCommentAdded,
                Title = "New Comment",
                Message = $"{comment.UserName} commented on task '{task.Title}'",
                RelatedEntityId = task.Id,
                RelatedEntityType = "Task",
                CreatedBy = userId
            };

            var dto = await CreateNotificationAsync(notification);
            createdNotifications.Add(dto);
        }

        return createdNotifications;
    }

    public async Task<NotificationDto?> NotifyTaskPriorityChangedAsync(Guid groupId, TaskDto task, string oldPriority, string newPriority)
    {
        if (!task.AssignedToId.HasValue)
            return null;

        if (!await ShouldNotifyAsync(task.AssignedToId.Value, NotificationType.TaskPriorityChanged))
        {
            _logger.LogInformation("User {UserId} has disabled TaskPriorityChanged notifications", task.AssignedToId.Value);
            return null;
        }

        var notification = new Notification
        {
            UserId = task.AssignedToId.Value,
            Type = NotificationType.TaskPriorityChanged,
            Title = "Task Priority Changed",
            Message = $"Task '{task.Title}' priority changed from {oldPriority} to {newPriority}",
            RelatedEntityId = task.Id,
            RelatedEntityType = "Task",
            CreatedBy = task.AssignedToId.Value
        };

        return await CreateNotificationAsync(notification);
    }

    public async Task<NotificationDto?> NotifyTaskUpdatedAsync(Guid groupId, TaskDto task)
    {
        if (!task.AssignedToId.HasValue || task.AssignedToId.Value == task.CreatedBy)
            return null;

        if (!await ShouldNotifyAsync(task.AssignedToId.Value, NotificationType.TaskUpdated))
        {
            _logger.LogInformation("User {UserId} has disabled TaskUpdated notifications", task.AssignedToId.Value);
            return null;
        }

        var notification = new Notification
        {
            UserId = task.AssignedToId.Value,
            Type = NotificationType.TaskUpdated,
            Title = "Task Updated",
            Message = $"Task '{task.Title}' has been updated",
            RelatedEntityId = task.Id,
            RelatedEntityType = "Task",
            CreatedBy = task.AssignedToId.Value
        };

        return await CreateNotificationAsync(notification);
    }

    public async Task<NotificationDto?> NotifyGroupInvitationAsync(Guid userId, string groupName)
    {
        if (!await ShouldNotifyAsync(userId, NotificationType.GroupInvitation))
        {
            _logger.LogInformation("User {UserId} has disabled GroupInvitation notifications", userId);
            return null;
        }

        var notification = new Notification
        {
            UserId = userId,
            Type = NotificationType.GroupInvitation,
            Title = "Group Joined",
            Message = $"You have joined the group: {groupName}",
            RelatedEntityType = "Group",
            CreatedBy = userId
        };

        return await CreateNotificationAsync(notification);
    }

    public async Task<Result<PaginatedList<NotificationDto>>> GetUserNotificationsAsync(Guid userId, bool unreadOnly = false, int pageNumber = 1, int pageSize = 20)
    {
        var query = _context.Notifications
            .Where(n => n.UserId == userId);

        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        var totalCount = await query.CountAsync();

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                UserId = n.UserId,
                Type = n.Type,
                TypeName = n.Type.ToString(),
                Title = n.Title,
                Message = n.Message,
                RelatedEntityId = n.RelatedEntityId,
                RelatedEntityType = n.RelatedEntityType,
                IsRead = n.IsRead,
                ReadAt = n.ReadAt,
                CreatedAt = n.CreatedAt
            })
            .Take(50)
            .ToListAsync();


        return new PaginatedList<NotificationDto>
        {
            Items = notifications,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<Result<UnreadCountDto>> GetUnreadCountAsync(Guid userId)
    {
        var count = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .CountAsync();
        
        return new UnreadCountDto { Count = count };
    }

    public async Task<Result> MarkAsReadAsync(Guid notificationId, Guid userId)
    {
        var updatedCount = await _context.Notifications
            .Where(n => n.Id == notificationId && n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, DateTime.UtcNow));

        _logger.LogInformation("{Count} notifications marked as read for user {UserId}", updatedCount, userId);

        return Result.Success();
    }

    public async Task<Result> MarkAllAsReadAsync(Guid userId)
    {
        var unreadNotifications = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var notification in unreadNotifications)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("All notifications marked as read for user {UserId}", userId);

        return Result.Success();
    }

    public async Task<Result> DeleteNotificationAsync(Guid notificationId, Guid userId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification == null)
            return Error.NotFound("Notification not found");

        notification.IsDeleted = true;
        notification.DeletedAt = DateTime.UtcNow;
        notification.DeletedBy = userId;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Notification {NotificationId} deleted by user {UserId}",
            notificationId, userId);

        return Result.Success();
    }

    public async Task<Result<List<NotificationPreferenceDto>>> GetUserPreferencesAsync(Guid userId)
    {
        var existingPreferences = await _context.NotificationPreferences
            .Where(np => np.UserId == userId)
            .ToListAsync();

        var allTypes = Enum.GetValues<NotificationType>();
        var preferences = new List<NotificationPreferenceDto>();

        foreach (var type in allTypes)
        {
            var existing = existingPreferences.FirstOrDefault(p => p.Type == type);

            preferences.Add(new NotificationPreferenceDto
            {
                Type = type,
                TypeName = type.ToString(),
                Description = GetNotificationTypeDescription(type),
                IsEnabled = existing?.IsEnabled ?? true,
                IsEmailEnabled = existing?.IsEmailEnabled ?? true,
                ReminderHoursBefore = existing?.ReminderHoursBefore ?? (type == NotificationType.TaskDueSoon ? 24 : null)
            });
        }

        return preferences;
    }

    public async Task<Result> SaveUserPreferencesAsync(Guid userId, UpdateNotificationPreferencesDto dto)
    {
        var types = dto.Preferences.Select(p => p.Type).ToList();

        var existingPreferences = await _context.NotificationPreferences
            .Where(np => np.UserId == userId && types.Contains(np.Type))
            .ToDictionaryAsync(np => np.Type);

        foreach (var item in dto.Preferences)
        {
            if (existingPreferences.TryGetValue(item.Type, out var preference))
            {
                preference.IsEnabled = item.IsEnabled;
                preference.IsEmailEnabled = item.IsEmailEnabled;
                preference.ReminderHoursBefore = item.ReminderHoursBefore;
                preference.UpdatedBy = userId;
                preference.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                _context.NotificationPreferences.Add(new NotificationPreference
                {
                    UserId = userId,
                    Type = item.Type,
                    IsEnabled = item.IsEnabled,
                    IsEmailEnabled = item.IsEmailEnabled,
                    ReminderHoursBefore = item.ReminderHoursBefore,
                    CreatedBy = userId
                });
            }
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("User {UserId} updated notification preferences", userId);

        return Result.Success();
    }
    public async Task<bool> ShouldNotifyAsync(Guid userId, NotificationType type)
    {
        var preference = await _context.NotificationPreferences
            .FirstOrDefaultAsync(np => np.UserId == userId && np.Type == type);

        return preference?.IsEnabled ?? true;
    }

    public async Task<bool> ShouldEmailAsync(Guid userId, NotificationType type)
    {
        var preference = await _context.NotificationPreferences
            .FirstOrDefaultAsync(np => np.UserId == userId && np.Type == type);

        return preference?.IsEmailEnabled ?? true;
    }

    public async Task<int> GetReminderHoursBeforeAsync(Guid userId)
    {
        var preference = await _context.NotificationPreferences
            .FirstOrDefaultAsync(np => np.UserId == userId && np.Type == NotificationType.TaskDueSoon);

        return preference?.ReminderHoursBefore ?? 24;
    }

    public async Task<(string Email, string UserName)?> GetUserContactAsync(Guid userId)
    {
        var user = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.UserName })
            .FirstOrDefaultAsync();

        if (user == null) return null;

        return (user.Email, user.UserName);
    }

    public async Task TrySendTaskAssignmentEmailAsync(Guid assigneeUserId, string taskTitle, string groupName, string assignedByUserName)
    {
        try
        {
            if (!await ShouldEmailAsync(assigneeUserId, NotificationType.TaskAssigned))
                return;

            var contact = await GetUserContactAsync(assigneeUserId);
            if (contact == null)
                return;

            await _emailService.SendTaskAssignmentEmailAsync(
                contact.Value.Email,
                contact.Value.UserName,
                taskTitle,
                groupName,
                assignedByUserName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send task assignment email to user {UserId}", assigneeUserId);
        }
    }

    public async Task TrySendCommentEmailAsync(Guid taskAssignedToUserId, Guid commenterId, string taskTitle, string commentContent)
    {
        try
        {
            if (taskAssignedToUserId == commenterId)
                return;

            if (!await ShouldEmailAsync(taskAssignedToUserId, NotificationType.TaskCommentAdded))
                return;

            var assignee = await GetUserContactAsync(taskAssignedToUserId);
            var commenter = await GetUserContactAsync(commenterId);

            if (assignee == null || commenter == null)
                return;

            await _emailService.SendTaskCommentEmailAsync(
                assignee.Value.Email,
                assignee.Value.UserName,
                commenter.Value.UserName,
                taskTitle,
                commentContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send comment email to user {UserId}", taskAssignedToUserId);
        }
    }

    private async Task<NotificationDto> CreateNotificationAsync(Notification notification)
    {
        _context.Notifications.Add(notification);
        var rows = await _context.SaveChangesAsync();
        _logger.LogInformation("SaveChanges result: {Rows} rows affected", rows);

        return new NotificationDto
        {
            Id = notification.Id,
            UserId = notification.UserId,
            Type = notification.Type,
            TypeName = notification.Type.ToString(),
            Title = notification.Title,
            Message = notification.Message,
            RelatedEntityId = notification.RelatedEntityId,
            RelatedEntityType = notification.RelatedEntityType,
            IsRead = notification.IsRead,
            ReadAt = notification.ReadAt,
            CreatedAt = notification.CreatedAt
        };
    }

    private string GetNotificationTypeDescription(NotificationType type)
    {
        return type switch
        {
            NotificationType.TaskAssigned => "When you are assigned to a task",
            NotificationType.TaskStatusChanged => "When a task status changes in your groups",
            NotificationType.TaskCommentAdded => "When someone comments on your tasks",
            NotificationType.TaskPriorityChanged => "When a task priority is changed",
            NotificationType.TaskUpdated => "When a task you're assigned to is updated",
            NotificationType.GroupInvitation => "When you join a new group",
            NotificationType.TaskDueSoon => "Reminder before task due date",
            NotificationType.TaskOverdue => "When your tasks become overdue",
            _ => type.ToString()
        };
    }
}