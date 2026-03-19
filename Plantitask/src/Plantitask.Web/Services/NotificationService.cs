using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

namespace Plantitask.Web.Services;

public class NotificationWebService : BaseApiService, INotificationWebService
{
    public NotificationWebService(HttpClient http) : base(http) { }

    public Task<ServiceResult<PaginatedResult<NotificationDto>>> GetNotificationsAsync(bool unreadOnly = false, int page = 1, int pageSize = 20)
        => GetAsync<PaginatedResult<NotificationDto>>($"api/notifications?unreadOnly={unreadOnly}&pageNumber={page}&pageSize={pageSize}");

    public Task<ServiceResult<UnreadCountModel>> GetUnreadCountAsync()
        => GetAsync<UnreadCountModel>("api/notifications/unread-count");

    public Task<ServiceResult<MessageResponse>> MarkAsReadAsync(Guid notificationId)
        => PatchAsync<MessageResponse>($"api/notifications/{notificationId}/read");

    public Task<ServiceResult<MessageResponse>> MarkAllAsReadAsync()
        => PutAsync<MessageResponse>("api/notifications/read-all", new {});

    public Task<ServiceResult<MessageResponse>> DeleteNotificationAsync(Guid notificationId)
        => DeleteAsync<MessageResponse>($"api/notifications/{notificationId}");
}
