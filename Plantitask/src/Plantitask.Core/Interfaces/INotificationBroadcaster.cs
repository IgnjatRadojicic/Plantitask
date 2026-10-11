using Plantitask.Contracts.DTO.Notifications;
namespace Plantitask.Core.Interfaces
{
    public interface INotificationBroadcaster
    {
        Task BroadcastNotificationAsync(NotificationDto notification);
        Task BroadcastToGroupAsync(Guid groupId, NotificationDto notification);
    }
}
