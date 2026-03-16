using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using TaskManagement.Web.Interfaces;
using TaskManagement.Web.Models;

namespace TaskManagement.Web.Services;

public class FieldSignalRService : IAsyncDisposable, IFieldSignalRService
{
    private HubConnection? _hub;
    private readonly IAuthService _authService;
    private readonly IConfiguration _configuration;

    public event Func<string, int, double, Task>? OnTreeUpdated;
    public event Func<FieldTreeDto, Task>? OnTreeAdded;

    public FieldSignalRService(IAuthService authService, IConfiguration configuration)
    {
        _authService = authService;
        _configuration = configuration;
    }

    public async Task ConnectAsync()
    {
        if (_hub != null) return;

        var token = await _authService.GetTokenAsync();
        if (string.IsNullOrEmpty(token)) return;

        var hubUrl = (_configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5212")
                     .TrimEnd('/') + "/hubs/notifications";

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(token);
            })
            .WithAutomaticReconnect()
            .Build();

        _hub.On<string, int, double>("TreeUpdated", async (groupId, newStage, completionPct) =>
        {
            if (OnTreeUpdated != null)
                await OnTreeUpdated.Invoke(groupId, newStage, completionPct);
        });

        _hub.On<FieldTreeDto>("TreeAdded", async (treeData) =>
        {
            if (OnTreeAdded != null)
                await OnTreeAdded.Invoke(treeData);
        });

        try
        {
            await _hub.StartAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SignalR connection failed: {ex.Message}");
        }
    }
    public async Task JoinGroupRoomsAsync(IEnumerable<string> groupIds)
    {
        if (_hub is null || _hub.State != HubConnectionState.Connected) return;

        foreach (var groupId in groupIds)
            await _hub.SendAsync("JoinGroupRoom", groupId);
    }

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    public async ValueTask DisposeAsync()
    {
        if (_hub != null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }
}