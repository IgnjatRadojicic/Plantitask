using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

using Plantitask.Core.Common;
using Plantitask.Core.DTO.Notifications;
namespace Plantitask.Web.Services;

public class NotificationService : BaseApiService, INotificationService
{
    public NotificationService(HttpClient http) : base(http) { }

    public Task<ServiceResult<PaginatedList<NotificationDto>>> GetNotificationsAsync(bool unreadOnly = false, int page = 1, int pageSize = 20)
        => GetAsync<PaginatedList<NotificationDto>>($"api/notifications?unreadOnly={unreadOnly}&pageNumber={page}&pageSize={pageSize}");

    public Task<ServiceResult<UnreadCountDto>> GetUnreadCountAsync()
        => GetAsync<UnreadCountDto>("api/notifications/unread-count");

    public Task<ServiceResult<MessageResponse>> MarkAsReadAsync(Guid notificationId)
        => PatchAsync<MessageResponse>($"api/notifications/{notificationId}/read");

    public Task<ServiceResult<MessageResponse>> MarkAllAsReadAsync()
        => PutAsync<MessageResponse>("api/notifications/read-all", new {});

    public Task<ServiceResult<MessageResponse>> DeleteNotificationAsync(Guid notificationId)
        => DeleteAsync<MessageResponse>($"api/notifications/{notificationId}");
}
