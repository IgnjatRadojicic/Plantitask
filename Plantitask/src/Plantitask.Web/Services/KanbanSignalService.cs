using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

namespace Plantitask.Web.Services;

public class KanbanSignalRService : IKanbanSignalRService
{
    private HubConnection? _hub;
    private Guid _currentGroupId;
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public event Func<KanbanTaskMovedEvent, Task>? OnTaskMoved;
    public event Func<string, int, double, Task>? OnTreeUpdated;
    public event Func<KanbanTaskCreatedEvent, Task>? OnTaskCreated;
    public event Func<KanbanTaskDeletedEvent, Task>? OnTaskDeleted;
    public event Func<KanbanTaskUpdatedEvent, Task>? OnTaskUpdated;

    public KanbanSignalRService(IAuthService authService, IConfiguration configuration)
    {
        _authService = authService;
        _configuration = configuration;
    }

    public async Task ConnectAsync(Guid groupId)
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (_hub is not null)
            {
                if (_currentGroupId == groupId && _hub.State == HubConnectionState.Connected)
                    return;

                await DisposeHub();
            }

            _currentGroupId = groupId;

            var token = await _authService.GetTokenAsync();
            if (string.IsNullOrEmpty(token))
                return;

            var hubUrl = (_configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5212")
                         .TrimEnd('/') + "/hubs/kanban";

            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(token)!;
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHandlers(_hub);

            _hub.Reconnected += async _ =>
            {
                try
                {
                    await _hub.SendAsync("JoinBoard", _currentGroupId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Kanban SignalR rejoin failed: {ex.Message}");
                }
            };

            await _hub.StartAsync();
            await _hub.SendAsync("JoinBoard", groupId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Kanban SignalR connection failed: {ex.Message}");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            await DisposeHub();
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _connectionLock.Dispose();
    }

    private void RegisterHandlers(HubConnection hub)
    {
        hub.On<KanbanTaskMovedEvent>("TaskMoved", async moved =>
        {
            if (OnTaskMoved is not null)
                await OnTaskMoved.Invoke(moved);
        });

        hub.On<string, int, double>("TreeUpdated", async (groupId, stage, pct) =>
        {
            if (OnTreeUpdated is not null)
                await OnTreeUpdated.Invoke(groupId, stage, pct);
        });

        hub.On<KanbanTaskCreatedEvent>("TaskCreated", async created =>
        {
            if (OnTaskCreated is not null)
                await OnTaskCreated.Invoke(created);
        });

        hub.On<KanbanTaskDeletedEvent>("TaskDeleted", async deleted =>
        {
            if (OnTaskDeleted is not null)
                await OnTaskDeleted.Invoke(deleted);
        });

        hub.On<KanbanTaskUpdatedEvent>("TaskUpdated", async updated =>
        {
            if (OnTaskUpdated is not null)
                await OnTaskUpdated.Invoke(updated);
        });
    }

    private async Task DisposeHub()
    {
        if (_hub is null)
            return;

        try
        {
            if (_hub.State == HubConnectionState.Connected)
                await _hub.SendAsync("LeaveBoard", _currentGroupId);
        }
        catch {}

        await _hub.DisposeAsync();
        _hub = null;
    }
}