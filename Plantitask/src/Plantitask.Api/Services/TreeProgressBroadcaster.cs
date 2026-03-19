using Microsoft.AspNetCore.SignalR;
using Plantitask.Api.Hubs;
using Plantitask.Api.Interfaces;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Services;

public class TreeProgressBroadcaster : ITreeProgressBroadcaster
{
    private readonly IHubContext<NotificationHub> _hub;
    private readonly IDashboardService _dashboardService;
    private readonly ILogger<TreeProgressBroadcaster> _logger;

    public TreeProgressBroadcaster(
        IHubContext<NotificationHub> hub,
        IDashboardService dashboardService,
        ILogger<TreeProgressBroadcaster> logger)
    {
        _hub = hub;
        _dashboardService = dashboardService;
        _logger = logger;
    }

    public async Task BroadcastTreeUpdateAsync(Guid groupId)
    {
        try
        {
            var result = await _dashboardService.GetGroupTreeProgressAsync(groupId);
            if (result.IsFailure)
            {
                _logger.LogWarning(
                    "Could not get tree progress for group {GroupId}: {Error}",
                    groupId, result.Error);
                return;
            }

            var tree = result.Value!;

            await _hub.Clients
                .Group($"group_{groupId}")
                .SendAsync("TreeUpdated",
                    groupId.ToString(),
                    (int)tree.CurrentTreeStage,
                    tree.CompletionPercentage);

            _logger.LogInformation(
                "Tree update broadcast to group {GroupId} — stage {Stage} at {Pct}%",
                groupId, tree.CurrentTreeStage, tree.CompletionPercentage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error broadcasting tree update to group {GroupId}", groupId);
        }
    }
}