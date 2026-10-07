using Plantitask.Web.Models;

using Plantitask.Core.DTO.Notifications;
namespace Plantitask.Web.Interfaces;

public interface INotificationSignalRService : IAsyncDisposable
{
    event Func<NotificationDto, Task>? OnNotificationReceived;
    Task ConnectAsync();
    Task JoinGroupRoomAsync(Guid groupId);
    Task LeaveGroupRoomAsync(Guid groupId);
    Task DisconnectAsync();
    bool IsConnected { get; }
}
