using Plantitask.Core.DTO.Notifications;
namespace Plantitask.Api.Interfaces
{
    public interface INotificationBroadcaster
    {
        Task BroadcastNotificationAsync(NotificationDto notification);
        Task BroadcastToGroupAsync(Guid groupId, NotificationDto notification);
    }
}
