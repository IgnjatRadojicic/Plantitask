namespace Plantitask.Web.Models;

// ===== Dashboard Dtos =====

public class PersonalDashboardDto
{
    public List<TaskSummaryDto> OverdueTasks { get; set; } = new();
    public List<TaskSummaryDto> DueToday { get; set; } = new();
    public List<TaskSummaryDto> DueThisWeek { get; set; } = new();
    public List<TaskSummaryDto> RecentlyCompleted { get; set; } = new();
    public List<ActivityDto> RecentActivity { get; set; } = new();
    public List<TrendPointDto> CompletionTrend { get; set; } = new();
    public int TotalAssignedTasks { get; set; }
    public int TotalCompletedTasks { get; set; }
    public int GroupCount { get; set; }
}

public class TaskSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string StatusColor { get; set; } = string.Empty;
    public string PriorityName { get; set; } = string.Empty;
    public string PriorityColor { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class ActivityDto
{
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class TrendPointDto
{
    public DateTime Date { get; set; }
    public int CompletedCount { get; set; }
}