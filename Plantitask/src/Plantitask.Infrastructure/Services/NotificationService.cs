using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Comments;
using Plantitask.Core.DTO.Notifications;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Projections;

namespace Plantitask.Infrastructure.Services;

/// <summary>
/// Creates in-app notifications, reads them back and manages per-type preferences. The Notify*
/// methods return the created DTOs so the controller can broadcast them over SignalR after the
/// commit; preference checks always default to enabled when no row exists.
/// </summary>
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

    /// <summary>
    /// Notifies the assignee about a new assignment. Null when there is nothing to send: no
    /// assignee, self-assignment, or the recipient turned this type off.
    /// </summary>
    public async Task<NotificationDto?> NotifyAssignmentAsync(Guid actorId, TaskDto task)
    {
        if (task.AssignedToId is not Guid assigneeId || assigneeId == actorId)
            return null;

        if (!await ShouldNotifyAsync(assigneeId, NotificationType.TaskAssigned))
        {
            _logger.LogInformation("User {UserId} has disabled TaskAssigned notifications", assigneeId);
            return null;
        }

        var notification = new Notification
        {
            UserId = assigneeId,
            ActorId = actorId,
            Type = NotificationType.TaskAssigned,
            Title = "Task Assigned",
            Message = $"You have been assigned to task: {task.Title}",
            RelatedEntityId = task.Id,
            RelatedEntityType = "Task",
        };

        return await CreateNotificationAsync(notification, await GetUserNameAsync(actorId));
    }

    /// <summary>
    /// Fans a status change out to the assignee and the creator, skipping the actor and anyone
    /// who disabled the type. Preferences are fetched in one query and everything commits with
    /// a single save - the shape that replaced the old save-per-recipient N+1.
    /// </summary>
    public async Task<List<NotificationDto>> NotifyTaskStatusChangedAsync(Guid actorId, TaskDto task, string oldStatus, string newStatus)
    {
        var recipients = new List<Guid>();

        if (task.AssignedToId.HasValue && task.AssignedToId.Value != actorId)
            recipients.Add(task.AssignedToId.Value);

        if (task.CreatedBy != actorId && task.CreatedBy != task.AssignedToId)
            recipients.Add(task.CreatedBy);

        if (recipients.Count == 0)
            return new();

        var disabledMembers = await _context.NotificationPreferences
            .Where(np => recipients.Contains(np.UserId)
                && np.Type == NotificationType.TaskStatusChanged
                && !np.IsEnabled)
            .Select(np => np.UserId)
            .ToHashSetAsync();

        var notifications = recipients
            .Where(recipientId => !disabledMembers.Contains(recipientId))
            .Select(recipientId => new Notification
            {
                UserId = recipientId,
                ActorId = actorId,
                Type = NotificationType.TaskStatusChanged,
                Title = "Task Status Changed",
                Message = $"Task '{task.Title}' status changed from {oldStatus} to {newStatus}",
                RelatedEntityId = task.Id,
                RelatedEntityType = "Task",
            })
            .ToList();

        if (notifications.Count == 0)
            return new();

        _context.Notifications.AddRange(notifications);
        await _context.SaveChangesAsync();

        var actorName = await GetUserNameAsync(actorId);

        return notifications.Select(n => ToDto(n, actorName)).ToList();
    }

    /// <summary>
    /// Fans a new comment out to the task's creator and assignee, minus the commenter. Same
    /// batched-preferences, single-save shape as the status fan-out.
    /// </summary>
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

        if (usersToNotify.Count == 0)
            return new();

        var disabled = await _context.NotificationPreferences
            .Where(np => usersToNotify.Contains(np.UserId)
                && np.Type == NotificationType.TaskCommentAdded
                && !np.IsEnabled)
            .Select(np => np.UserId)
            .ToHashSetAsync();

        var notifications = usersToNotify
            .Where(userId => !disabled.Contains(userId))
            .Select(userId => new Notification
            {
                UserId = userId,
                ActorId = comment.UserId,
                Type = NotificationType.TaskCommentAdded,
                Title = "New Comment",
                Message = $"New comment on task '{task.Title}'",
                RelatedEntityId = task.Id,
                RelatedEntityType = "Task",
            })
            .ToList();

        if (notifications.Count == 0)
            return new();

        _context.Notifications.AddRange(notifications);
        await _context.SaveChangesAsync();

        return notifications.Select(n => ToDto(n, comment.UserName)).ToList();
    }

    /// <summary>
    /// Notifies the assignee that the priority moved, carrying both names in the message.
    /// Null when there is no assignee, the actor is the assignee, or the type is disabled.
    /// </summary>
    public async Task<NotificationDto?> NotifyTaskPriorityChangedAsync(Guid actorId, TaskDto task, string oldPriority, string newPriority)
    {
        if (task.AssignedToId is not Guid assigneeId || assigneeId == actorId)
            return null;

        if (!await ShouldNotifyAsync(assigneeId, NotificationType.TaskPriorityChanged))
        {
            _logger.LogInformation("User {UserId} has disabled TaskPriorityChanged notifications", assigneeId);
            return null;
        }

        var notification = new Notification
        {
            UserId = assigneeId,
            ActorId = actorId,
            Type = NotificationType.TaskPriorityChanged,
            Title = "Task Priority Changed",
            Message = $"Task '{task.Title}' priority changed from {oldPriority} to {newPriority}",
            RelatedEntityId = task.Id,
            RelatedEntityType = "Task",
            
        };

        return await CreateNotificationAsync(notification, await GetUserNameAsync(actorId));
    }

    /// <summary>
    /// Tells the assignee their task was edited. Same null-when-nothing-to-send contract as the
    /// other single-recipient notifiers.
    /// </summary>
    public async Task<NotificationDto?> NotifyTaskUpdatedAsync(Guid actorId, TaskDto task)
    {
        if (task.AssignedToId is not Guid assigneeId || assigneeId == actorId)
            return null;

        if (!await ShouldNotifyAsync(assigneeId, NotificationType.TaskUpdated))
        {
            _logger.LogInformation("User {UserId} has disabled TaskUpdated notifications", assigneeId);
            return null;
        }

        var notification = new Notification
        {
            UserId = assigneeId,
            ActorId = actorId,
            Type = NotificationType.TaskUpdated,
            Title = "Task Updated",
            Message = $"Task '{task.Title}' has been updated",
            RelatedEntityId = task.Id,
            RelatedEntityType = "Task",
            
        };

        return await CreateNotificationAsync(notification, await GetUserNameAsync(actorId));
    }

    /// <summary>
    /// Confirms to the user that they joined a group. No actor - the user did this themselves.
    /// </summary>
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
        };

        return await CreateNotificationAsync(notification, null);
    }

    /// <summary>
    /// The caller's notifications, newest first, optionally unread only, through the shared
    /// projection and pagination contract.
    /// </summary>
    public async Task<Result<PaginatedList<NotificationDto>>> GetUserNotificationsAsync(Guid userId, bool unreadOnly = false, int pageNumber = 1, int pageSize = 20)
    {
        var query = _context.Notifications
            .Where(n => n.UserId == userId);

        if (unreadOnly)
            query = query.Where(n => !n.IsRead);


        return await query.OrderByDescending(n => n.CreatedAt)
            .Select(NotificationProjections.ToDto)
            .ToPaginatedListAsync(pageNumber, pageSize);

    }

    /// <summary>The number for the bell badge.</summary>
    public async Task<Result<UnreadCountDto>> GetUnreadCountAsync(Guid userId)
    {
        var count = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .CountAsync();
        
        return new UnreadCountDto { Count = count };
    }

    /// <summary>
    /// Marks one notification read with a single UPDATE scoped to the owner, so someone else's
    /// id is a silent no-op rather than a probe result. Sets UpdatedAt by hand because
    /// ExecuteUpdate bypasses the stamping override.
    /// </summary>
    public async Task<Result> MarkAsReadAsync(Guid notificationId, Guid userId)
    {
        var now = DateTime.UtcNow;

        var updatedCount = await _context.Notifications
            .Where(n => n.Id == notificationId && n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, now)
                .SetProperty(n => n.UpdatedAt, now));

        _logger.LogInformation("{Count} notifications marked as read for user {UserId}", updatedCount, userId);

        return Result.Success();
    }

    /// <summary>Marks everything unread as read in one set-based UPDATE.</summary>
    public async Task<Result> MarkAllAsReadAsync(Guid userId)
    {
        var now = DateTime.UtcNow;

        var updatedCount = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters
            .SetProperty(n => n.IsRead, true)
            .SetProperty(n => n.ReadAt, now)
            .SetProperty(n => n.UpdatedAt, now));

        _logger.LogInformation("Marked {Count} notifications as read for user {UserId}", updatedCount, userId);

        return Result.Success();
    }

    /// <summary>
    /// Soft-deletes one of the caller's own notifications; anyone else's id comes back as
    /// NotFound because the owner filter is part of the lookup.
    /// </summary>
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

    /// <summary>
    /// The full preference sheet: every notification type, merged with the user's stored rows.
    /// Types with no row show the defaults (enabled, 24 hours before for due-soon).
    /// </summary>
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

    /// <summary>
    /// Upserts preference rows in one save. The whole batch is validated before anything is
    /// touched - the type is a client-supplied enum so it gets Enum.IsDefined, and the reminder
    /// hours are clamped to a sane range (null stays allowed and means no override).
    /// </summary>
    public async Task<Result> SaveUserPreferencesAsync(Guid userId, UpdateNotificationPreferencesDto dto)
    {
        foreach (var item in dto.Preferences)
        {
            if (!Enum.IsDefined(item.Type))
                return Error.BadRequest("Unknown notification type");

            if (item.ReminderHoursBefore is < 1 or > 168)
                return Error.Validation("Reminder hours must be between 1 and 168");
        }

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
                    
                });
            }
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("User {UserId} updated notification preferences", userId);

        return Result.Success();
    }
    /// <summary>
    /// Whether an in-app notification of this type should be created. No stored row means yes -
    /// notifications are opt-out.
    /// </summary>
    public async Task<bool> ShouldNotifyAsync(Guid userId, NotificationType type)
    {
        return await _context.NotificationPreferences
            .Where(np => np.UserId == userId && np.Type == type)
            .Select(np => (bool?)np.IsEnabled)
            .FirstOrDefaultAsync() ?? true;
    }

    /// <summary>Same opt-out contract as <see cref="ShouldNotifyAsync"/>, for the email channel.</summary>
    public async Task<bool> ShouldEmailAsync(Guid userId, NotificationType type)
    {
        return await _context.NotificationPreferences
            .Where(np => np.UserId == userId && np.Type == type)
            .Select(np => (bool?)np.IsEmailEnabled)
            .FirstOrDefaultAsync() ?? true;
    }

    /// <summary>
    /// How many hours before the due date the reminder should fire, defaulting to 24. Read at
    /// scheduling time, so changing it affects future reminders, not ones already queued.
    /// </summary>
    public async Task<int> GetReminderHoursBeforeAsync(Guid userId)
    {
        return await _context.NotificationPreferences
            .Where(np => np.UserId == userId && np.Type == NotificationType.TaskDueSoon)
            .Select(np => np.ReminderHoursBefore)
            .FirstOrDefaultAsync() ?? 24;
    }

    /// <summary>Just the email and username, for building outbound mail.</summary>
    public async Task<(string Email, string UserName)?> GetUserContactAsync(Guid userId)
    {
        var user = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.UserName })
            .FirstOrDefaultAsync();

        if (user == null) return null;

        return (user.Email, user.UserName);
    }

    /// <summary>
    /// Best-effort assignment email, honoring the email preference. Runs after the mutation is
    /// committed, so failures are logged and swallowed - the Try prefix is the contract.
    /// </summary>
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

    /// <summary>
    /// Best-effort email to the assignee about a new comment, skipping self-comments and
    /// unassigned tasks. Same log-and-swallow contract as the other Try sender.
    /// </summary>
    public async Task TrySendCommentEmailAsync(Guid taskAssignedToUserId, Guid commenterId, string taskTitle, string commentContent)
    {
        try
        {
            if (taskAssignedToUserId == Guid.Empty || taskAssignedToUserId == commenterId)
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

    /// <summary>Saves a single notification and maps it, with the actor name supplied by the caller.</summary>
    private async Task<NotificationDto> CreateNotificationAsync(Notification notification, string? actorName)
    {
        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        return ToDto(notification, actorName);
    }

    /// <summary>Display name of the acting user, for the notifications they trigger.</summary>
    private Task<string?> GetUserNameAsync(Guid userId) =>
        _context.Users
            .Where(u => u.Id == userId)
            .Select(u => (string?)u.UserName)
            .FirstOrDefaultAsync();

    // Same property list as the query side. The create paths know the actor name already
    // because the Actor navigation is not loaded on a freshly added entity.
    private static readonly Func<Notification, NotificationDto> MapNotification =
        NotificationProjections.ToDto.Compile();

    private static NotificationDto ToDto(Notification n, string? actorName)
    {
        var dto = MapNotification(n);
        dto.ActorName = actorName;
        return dto;
    }

    /// <summary>Human-readable label for each type on the preferences screen.</summary>
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