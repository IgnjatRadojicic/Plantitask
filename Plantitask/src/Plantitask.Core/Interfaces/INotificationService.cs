using Plantitask.Core.Common;
using Plantitask.Contracts.Common;
using Plantitask.Contracts.DTO.Notifications;
using Plantitask.Contracts.Enums;
using Plantitask.Core.DTO.Tasks;

namespace Plantitask.Core.Interfaces;

public interface INotificationService
{
    Task<NotificationDto?> NotifyAssignmentAsync(Guid actorId, TaskSnapshot task);
    Task<NotificationDto?> NotifyUnassignedAsync(Guid actorId, TaskSnapshot task, Guid previousAssigneeId);
    Task<List<NotificationDto>> NotifyTaskStatusChangedAsync(Guid actorId, TaskSnapshot task, Guid createdById, string oldStatus, string newStatus);
    Task<List<NotificationDto>> NotifyTaskCommentAddedAsync(Guid actorId, TaskSnapshot task, Guid createdById);
    Task<NotificationDto?> NotifyTaskPriorityChangedAsync(Guid actorId, TaskSnapshot task, string oldPriority, string newPriority);
    Task<NotificationDto?> NotifyTaskUpdatedAsync(Guid actorId, TaskSnapshot task);
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
}