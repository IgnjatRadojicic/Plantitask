using System.ComponentModel.DataAnnotations;

namespace Plantitask.Web.Models
{

    // ===== Task Moduls =====


    public enum TaskDetailViewMode
    {
        FullScreen,
        SidePanel
    }
    public class TaskFilterDto
    {
        public int? StatusId { get; set; }
        public int? PriorityId { get; set; }
        public Guid? AssignedToUserId { get; set; }
        public bool? IsOverDue { get; set; }
        public string? SearchTerm { get; set; }

        public string ToQueryString()
        {
            var parts = new List<string>();

            if (StatusId.HasValue)
                parts.Add($"statusId={StatusId.Value}");
            if (PriorityId.HasValue)
                parts.Add($"priorityId={PriorityId.Value}");
            if (AssignedToUserId.HasValue)
                parts.Add($"assignedToUserId={AssignedToUserId.Value}");
            if (IsOverDue.HasValue)
                parts.Add($"isOverDue={IsOverDue.Value}");
            if (!string.IsNullOrWhiteSpace(SearchTerm))
                parts.Add($"searchTerm={Uri.EscapeDataString(SearchTerm)}");

            return parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
        }
    }

    public class TaskPriorityChangeResultDto
    {
        public TaskItemDto Task { get; set; } = null!;
        public string OldPriority { get; set; } = string.Empty;
        public string NewPriority { get; set; } = string.Empty;
    }


    public class TaskStatusChangeResultDto
    {
        public TaskItemDto Task { get; set; } = null!;
        public string OldStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;
    }

    public class ChangeTaskStatusDto
    {
        public int NewStatusId { get; set; }
    }

    public class AssignTaskDto
    {
        public Guid UserId { get; set; }
    }

    public class UpdateTaskDto
    {
        [StringLength(200, MinimumLength = 3)]
        public string? Title { get; set; }

        [StringLength(2000)]
        public string? Description { get; set; }

        [Range(1, 4)]
        public int? PriorityId { get; set; }

        public DateTime? DueDate { get; set; }
    }
    public class CreateTaskDto
    {
        [Required]
        [StringLength(200, MinimumLength = 3, ErrorMessage = "Title must be between 3 and 200 characters.")]
        public string Title { get; set; } = string.Empty;

        [StringLength(2000, ErrorMessage = "Description cannot exceed 2000 characters.")]
        public string Description { get; set; } = string.Empty;

        [Required]
        [Range(1, 4, ErrorMessage = "Priority must be between 1 and 4.")]
        public int PriorityId { get; set; } = 2;

        public DateTime? DueDate { get; set; }
        public Guid? AssignedToUserId { get; set; }
    }

    public class MoveTaskDto
    {
        public int NewStatusId { get; set; }
        public int NewDisplayOrder { get; set; }
    }

    public class TaskItemDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Guid GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public int StatusId { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public string StatusDisplayName { get; set; } = string.Empty;
        public string? StatusColor { get; set; }
        public int PriorityId { get; set; }
        public string PriorityName { get; set; } = string.Empty;
        public string? PriorityColor { get; set; }
        public Guid? AssignedToId { get; set; }
        public string? AssignedToUserName { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid CreatedBy { get; set; }
        public string CreatedByUserName { get; set; } = string.Empty;
        public int AttachmentCount { get; set; }
    }
}
