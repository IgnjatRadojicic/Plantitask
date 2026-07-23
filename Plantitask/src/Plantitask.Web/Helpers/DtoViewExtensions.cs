using Plantitask.Core.DTO.Attachments;
using Plantitask.Core.DTO.Kanban;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.DTO.Users;
using Plantitask.Core.Enums;

namespace Plantitask.Web.Helpers;

public static class DtoViewExtensions
{
    public static bool IsOverdue(this KanbanTaskDto task) =>
        task.DueDate.HasValue
        && task.DueDate.Value.Date < DateTime.UtcNow.Date
        && task.StatusId != (int)TaskStatusItem.Completed;

    public static string FileSizeDisplay(this AttachmentDto attachment) => attachment.FileSize switch
    {
        < 1024 => $"{attachment.FileSize} B",
        < 1048576 => $"{attachment.FileSize / 1024.0:F1} KB",
        < 1073741824 => $"{attachment.FileSize / 1048576.0:F1} MB",
        _ => $"{attachment.FileSize / 1073741824.0:F2} GB"
    };

    public static string DisplayName(this UserProfileDto profile)
    {
        var full = $"{profile.FirstName} {profile.LastName}".Trim();
        return string.IsNullOrEmpty(full) ? profile.UserName : full;
    }

    public static string Initials(this UserProfileDto profile)
    {
        if (!string.IsNullOrEmpty(profile.FirstName) && !string.IsNullOrEmpty(profile.LastName))
            return $"{profile.FirstName[0]}{profile.LastName[0]}".ToUpper();
        if (!string.IsNullOrEmpty(profile.UserName) && profile.UserName.Length >= 2)
            return profile.UserName[..2].ToUpper();
        return "?";
    }

    public static string ToQueryString(this TaskFilterDto filter)
    {
        var parts = new List<string>();

        if (filter.StatusId.HasValue)
            parts.Add($"statusId={filter.StatusId.Value}");
        if (filter.PriorityId.HasValue)
            parts.Add($"priorityId={filter.PriorityId.Value}");
        if (filter.AssignedToUserId.HasValue)
            parts.Add($"assignedToUserId={filter.AssignedToUserId.Value}");
        if (filter.IsOverDue.HasValue)
            parts.Add($"isOverDue={filter.IsOverDue.Value}");
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            parts.Add($"searchTerm={Uri.EscapeDataString(filter.SearchTerm)}");

        return parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
    }
}
