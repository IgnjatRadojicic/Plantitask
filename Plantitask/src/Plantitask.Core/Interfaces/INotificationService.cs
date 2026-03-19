using Plantitask.Core.Common;
using Plantitask.Core.DTO.Comments;
using Plantitask.Core.DTO.Notifications;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Enums;

namespace Plantitask.Core.Interfaces;

public interface INotificationService
{
    Task<NotificationDto?> NotifyTaskCreatedAsync(Guid createdByUserId, TaskDto task);
    Task<NotificationDto?> NotifyTaskAssignedAsync(Guid userId, TaskDto task);
    Task<List<NotificationDto>> NotifyTaskStatusChangedAsync(Guid groupId, TaskDto task, string oldStatus, string newStatus);
    Task<List<NotificationDto>> NotifyTaskCommentAddedAsync(Guid groupId, TaskDto task, CommentDto comment);
    Task<NotificationDto?> NotifyTaskPriorityChangedAsync(Guid groupId, TaskDto task, string oldPriority, string newPriority);
    Task<NotificationDto?> NotifyTaskUpdatedAsync(Guid groupId, TaskDto task);
    Task<NotificationDto?> NotifyGroupInvitationAsync(Guid userId, string groupName);

    Task<Result<PaginatedList<NotificationDto>>> GetUserNotificationsAsync(Guid userId, bool unreadOnly = false, int pageNumber = 1, int pageSize = 20);
    Task<Result<UnreadCountDto>> GetUnreadCountAsync(Guid userId);
    Task<Result> MarkAsReadAsync(Guid notificationId, Guid userId);
    Task<Result> MarkAllAsReadAsync(Guid userId);
    Task<Result> DeleteNotificationAsync(Guid notificationId, Guid userId);

    Task<Result<List<NotificationPreferenceDto>>> GetUserPreferencesAsync(Guid userId);
    Task<Result> SaveUserPreferencesAsync(Guid userId, UpdateNotificationPreferencesDto dto);

    Task<bool> ShouldNotifyAsync(Guid userId, NotificationType type);
    Task<bool> ShouldEmailAsync(Guid userId, NotificationType type);
    Task<int> GetReminderHoursBeforeAsync(Guid userId);
    Task<(string Email, string UserName)?> GetUserContactAsync(Guid userId);
    Task TrySendTaskAssignmentEmailAsync(Guid assigneeUserId, string taskTitle, string groupName, string assignedByUserName);
    Task TrySendCommentEmailAsync(Guid taskAssignedToUserId, Guid commenterId, string taskTitle, string commentContent);
}