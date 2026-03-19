namespace Plantitask.Web.Models;

// ===== Group Dtos =====

public class GroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public bool IsPasswordProtected { get; set; }
    public int MemberCount { get; set; }
    public int UserRole { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GroupDetailsDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GroupCode { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public bool IsPasswordProtected { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<GroupMemberDto> Members { get; set; } = new();
}

public class GroupMemberDto
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
}

public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Password { get; set; }
}

public class JoinGroupRequest
{
    public string GroupCode { get; set; } = string.Empty;
    public string? Password { get; set; }
}

public enum TreeStage
{
    EmptySoil = 0,
    Seed = 1,
    Sprout = 2,
    Sapling = 3,
    YoungTree = 4,
    FullTree = 5,
    FloweringTree = 6
}

public class FieldTreeDto
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public double CompletionPercentage { get; set; }
    public TreeStage CurrentTreeStage { get; set; }
    public int MemberCount { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
}


public class GroupStatisticsModel
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public double CompletionPercentage { get; set; }
    public TreeStage CurrentTreeStage { get; set; }

    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int InProgressTasks { get; set; }
    public int NotStartedTasks { get; set; }
    public int UnderReviewTasks { get; set; }
    public int OverdueTasks { get; set; }
    public int MemberCount { get; set; }
    public double? AverageCompletionDays { get; set; }

    public List<StatusCountModel> TasksByStatus { get; set; } = new();
    public List<PriorityCountModel> TasksByPriority { get; set; } = new();
    public List<MemberWorkloadModel> MemberWorkload { get; set; } = new();
    public List<TrendPointModel> CompletionTrend { get; set; } = new();
}

public class StatusCountModel
{
    public string StatusName { get; set; } = string.Empty;
    public string? Color { get; set; }
    public int Count { get; set; }
}

public class PriorityCountModel
{
    public string PriorityName { get; set; } = string.Empty;
    public string? Color { get; set; }
    public int Count { get; set; }
}

public class MemberWorkloadModel
{
    public string UserName { get; set; } = string.Empty;
    public int AssignedCount { get; set; }
    public int CompletedCount { get; set; }
    public int OverdueCount { get; set; }
}

public class TrendPointModel
{
    public DateTime Date { get; set; }
    public int CompletedCount { get; set; }
}


public class FieldTreeViewDto
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public double CompletionPercentage { get; set; }
    public TreeStage CurrentTreeStage { get; set; }
    public int MemberCount { get; set; }
    public int TotalTasks { get; set; }
    public int CompletedTasks { get; set; }
    public double X { get; set; }
    public double Y { get; set; }

    public static FieldTreeViewDto FromDto(FieldTreeDto dto, double x, double y) => new()
    {
        GroupId = dto.GroupId,
        GroupName = dto.GroupName,
        CompletionPercentage = dto.CompletionPercentage,
        CurrentTreeStage = dto.CurrentTreeStage,
        MemberCount = dto.MemberCount,
        TotalTasks = dto.TotalTasks,
        CompletedTasks = dto.CompletedTasks,
        X = x,
        Y = y
    };
}