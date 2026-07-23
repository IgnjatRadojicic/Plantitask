
namespace Plantitask.Core.DTO.Dashboard
{
    public class PersonalDashboardDto
    {
        public List<TaskSummaryDto> OverdueTasks { get; set; } = new();
        public List<TaskSummaryDto> DueToday { get; set; } = new();
        public List<TaskSummaryDto> DueThisWeek { get; set; } = new();
        public List<TaskSummaryDto> RecentlyCompleted { get; set; } = new();
        public List<TrendPointDto> CompletionTrend { get; set; } = new();

        public List<ActivityDto> RecentActivity { get; set; } = new();
        public int TotalAssignedTasks { get; set; }
        public int TotalCompletedTasks { get; set; }
        public int GroupCount { get; set; }

    }
}
