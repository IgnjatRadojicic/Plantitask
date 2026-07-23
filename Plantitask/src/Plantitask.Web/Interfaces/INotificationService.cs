using Plantitask.Web.Models;

using Plantitask.Core.Common;
using Plantitask.Core.DTO.Notifications;
namespace Plantitask.Web.Interfaces;

public interface INotificationService
{
    Task<ServiceResult<PaginatedList<NotificationDto>>> GetNotificationsAsync(bool unreadOnly = false, int page = 1, int pageSize = 20);
    Task<ServiceResult<UnreadCountDto>> GetUnreadCountAsync();
    Task<ServiceResult<MessageResponse>> MarkAsReadAsync(Guid notificationId);
    Task<ServiceResult<MessageResponse>> MarkAllAsReadAsync();
    Task<ServiceResult<MessageResponse>> DeleteNotificationAsync(Guid notificationId);
}
