using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

namespace Plantitask.Web.Services;

public class FieldSignalRService : IAsyncDisposable, IFieldSignalRService
{
    private HubConnection? _hub;
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private HashSet<string> _joinedGroupIds = new();

    public event Func<string, int, double, Task>? OnTreeUpdated;
    public event Func<FieldTreeDto, Task>? OnTreeAdded;

    public FieldSignalRService(IAuthService authService, IConfiguration configuration)
    {
        _authService = authService;
        _configuration = configuration;
    }

    public async Task ConnectAsync()
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (_hub is not null && _hub.State == HubConnectionState.Connected)
                return;

            if (_hub is not null)
                await DisposeHub();

            var token = await _authService.GetTokenAsync();
            if (string.IsNullOrEmpty(token)) return;

            var hubUrl = (_configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5212")
                         .TrimEnd('/') + "/hubs/notifications";

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
                    await RejoinGroupsAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Field SignalR rejoin failed: {ex.Message}");
                }
            };

            await _hub.StartAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Field SignalR connection failed: {ex.Message}");
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task JoinGroupRoomsAsync(IEnumerable<string> groupIds)
    {
        if (_hub is null || _hub.State != HubConnectionState.Connected) return;

        foreach (var groupId in groupIds)
        {
            await _hub.SendAsync("JoinGroupRoom", groupId);
            _joinedGroupIds.Add(groupId);
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
        hub.On<string, int, double>("TreeUpdated", async (groupId, newStage, completionPct) =>
        {
            if (OnTreeUpdated is not null)
                await OnTreeUpdated.Invoke(groupId, newStage, completionPct);
        });

        hub.On<FieldTreeDto>("TreeAdded", async (treeData) =>
        {
            if (OnTreeAdded is not null)
                await OnTreeAdded.Invoke(treeData);
        });
    }

    private async Task RejoinGroupsAsync()
    {
        if (_hub is null || _hub.State != HubConnectionState.Connected) return;

        foreach (var groupId in _joinedGroupIds)
            await _hub.SendAsync("JoinGroupRoom", groupId);
    }

    private async Task DisposeHub()
    {
        if (_hub is null) return;

        try
        {
            if (_hub.State == HubConnectionState.Connected)
            {
                foreach (var groupId in _joinedGroupIds)
                    await _hub.SendAsync("LeaveGroupRoom", groupId);
            }
        }
        catch {}

        await _hub.DisposeAsync();
        _hub = null;
        _joinedGroupIds.Clear();
    }
}