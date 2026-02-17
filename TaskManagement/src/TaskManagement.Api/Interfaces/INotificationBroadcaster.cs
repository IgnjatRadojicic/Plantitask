using TaskManagement.Core.DTO.Comments;
using TaskManagement.Core.DTO.Notifications;
using TaskManagement.Core.DTO.Tasks;
namespace TaskManagement.Api.Interfaces
{
    public interface INotificationBroadcaster
    {
        Task<NotificationDto> NotifyTaskAssignedAsync(Guid userId, TaskDto task);
        Task<NotificationDto> NotifyTaskStatusChangedAsync(Guid groupId, TaskDto task, string oldStatus, string newStatus);
        Task<NotificationDto> NotifyTaskCommentAddedAsync(Guid groupId, TaskDto task, CommentDto comment);
        Task<NotificationDto> NotifyTaskPriorityChangedAsync(Guid groupId, TaskDto task, string oldPriority, string newPriority);
        Task<NotificationDto> NotifyTaskUpdatedAsync(Guid groupId, TaskDto task);
        Task<NotificationDto> NotifyGroupInvitationAsync(Guid userId, string groupName);

        Task<List<NotificationDto>> GetUserNotificationsAsync(Guid userId, bool unreadOnly = false);
        Task<int> GetUnreadCountAsync(Guid userId);
        Task MarkAsReadAsync(Guid notificationId, Guid userId);      
        Task MarkAllAsReadAsync(Guid userId);                        
        Task DeleteNotificationAsync(Guid notificationId, Guid userId);
    }
}
