using Plantitask.Core.Enums;

namespace Plantitask.Core.DTO.Dashboard
{
    public class GroupStatisticsDto
    {
        public Guid GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public double CompletionPercentage { get; set; }
        public TreeStage CurrentTreeStage { get; set; }

        // Counts
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int InProgressTasks { get; set; }
        public int NotStartedTasks { get; set; }
        public int UnderReviewTasks { get; set; }
        public int OverdueTasks { get; set; }
        public int MemberCount { get; set; }
        public double? AverageCompletionDays { get; set; }

        // Chart data
        public List<StatusCountDto> TasksByStatus { get; set; } = new();
        public List<PriorityCountDto> TasksByPriority { get; set; } = new();
        public List<MemberWorkloadDto> MemberWorkload { get; set; } = new();
        public List<TrendPointDto> CompletionTrend { get; set; } = new();
    }

    public class StatusCountDto
    {
        public string StatusName { get; set; } = string.Empty;
        public string? Color { get; set; }
        public int Count { get; set; }
    }

    public class PriorityCountDto
    {
        public string PriorityName { get; set; } = string.Empty;
        public string? Color { get; set; }
        public int Count { get; set; }
    }

    public class MemberWorkloadDto
    {
        public string UserName { get; set; } = string.Empty;
        public int AssignedCount { get; set; }
        public int CompletedCount { get; set; }
        public int OverdueCount { get; set; }
    }

    public class TrendPointDto
    {
        public DateTime Date { get; set; }
        public int CompletedCount { get; set; }
    }
}