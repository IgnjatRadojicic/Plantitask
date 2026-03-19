using Plantitask.Web.Models;

namespace Plantitask.Web.Interfaces;

public interface INotificationSignalRService : IAsyncDisposable
{
    event Func<NotificationDto, Task>? OnNotificationReceived;
    Task ConnectAsync();
    Task JoinGroupRoomAsync(string groupId);
    Task LeaveGroupRoomAsync(string groupId);
    Task DisconnectAsync();
    bool IsConnected { get; }
}
