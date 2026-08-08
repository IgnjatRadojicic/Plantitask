using Microsoft.AspNetCore.SignalR;
using Plantitask.Api.Hubs;
using Plantitask.Api.Interfaces;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Services;

/// <summary>
/// Recomputes a group's tree progress and pushes it into the group's SignalR room whenever
/// tasks change. This is the one sanctioned caller of the unauthorized
/// GetGroupTreeProgressAsync read - the membership-gated room is what makes that safe.
/// </summary>
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

    /// <summary>
    /// Fetches fresh stage and percentage and emits TreeUpdated to the group room. Failures are
    /// logged and swallowed - the tree self-corrects on the next page load, so a broadcast is
    /// never worth failing the mutation that triggered it.
    /// </summary>
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