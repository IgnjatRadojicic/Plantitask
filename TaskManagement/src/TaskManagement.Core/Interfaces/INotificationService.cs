using TaskManagement.Core.DTO.Comments;
using TaskManagement.Core.DTO.Notifications;
using TaskManagement.Core.DTO.Tasks;
using TaskManagement.Core.Enums;

namespace TaskManagement.Core.Interfaces;

public interface INotificationService
{
    Task<NotificationDto> NotifyTaskAssignedAsync(Guid userId, TaskDto task);
    Task<List<NotificationDto>> NotifyTaskStatusChangedAsync(Guid groupId, TaskDto task, string oldStatus, string newStatus);
    Task<List<NotificationDto>> NotifyTaskCommentAddedAsync(Guid groupId, TaskDto task, CommentDto comment);
    Task<NotificationDto?> NotifyTaskPriorityChangedAsync(Guid groupId, TaskDto task, string oldPriority, string newPriority);
    Task<NotificationDto?> NotifyTaskUpdatedAsync(Guid groupId, TaskDto task);
    Task<NotificationDto> NotifyGroupInvitationAsync(Guid userId, string groupName);
    Task<NotificationDto?> NotifyTaskCreatedAsync(Guid createdByUserId, TaskDto task);
    Task<List<NotificationDto>> GetUserNotificationsAsync(Guid userId, bool unreadOnly = false);
    Task<int> GetUnreadCountAsync(Guid userId);
    Task MarkAsReadAsync(Guid notificationId, Guid userId);
    Task MarkAllAsReadAsync(Guid userId);
    Task DeleteNotificationAsync(Guid notificationId, Guid userId);

    Task<List<NotificationPreferenceDto>> GetUserPreferencesAsync(Guid userId);
    Task SaveUserPreferencesAsync(Guid userId, UpdateNotificationPreferencesDto dto);

    Task<bool> ShouldNotifyAsync(Guid userId, NotificationType type);

    Task<int> GetReminderHoursBeforeAsync(Guid userId);
}