using Plantitask.Web.Models;

namespace Plantitask.Web.Interfaces;


public interface IKanbanSignalRService : IAsyncDisposable
{
    event Func<KanbanTaskMovedEvent, Task>? OnTaskMoved;
    event Func<string, int, double, Task>? OnTreeUpdated;
    event Func<KanbanTaskCreatedEvent, Task>? OnTaskCreated;
    event Func<KanbanTaskDeletedEvent, Task>? OnTaskDeleted;
    event Func<KanbanTaskUpdatedEvent, Task>? OnTaskUpdated;

    Task ConnectAsync(Guid groupId);
    Task DisconnectAsync();
    bool IsConnected { get; }
}