using Plantitask.Web.Models;

namespace Plantitask.Web.Interfaces;

public interface INotificationWebService
{
    Task<ServiceResult<PaginatedResult<NotificationDto>>> GetNotificationsAsync(bool unreadOnly = false, int page = 1, int pageSize = 20);
    Task<ServiceResult<UnreadCountModel>> GetUnreadCountAsync();
    Task<ServiceResult<MessageResponse>> MarkAsReadAsync(Guid notificationId);
    Task<ServiceResult<MessageResponse>> MarkAllAsReadAsync();
    Task<ServiceResult<MessageResponse>> DeleteNotificationAsync(Guid notificationId);
}
