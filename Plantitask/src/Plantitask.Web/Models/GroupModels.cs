using Plantitask.Core.DTO.Dashboard;
using Plantitask.Core.Enums;

namespace Plantitask.Web.Models;

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
