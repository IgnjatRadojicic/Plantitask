namespace Plantitask.Core.DTO.Kanban;

public class KanbanTaskDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int StatusId { get; set; }
    public int PriorityId { get; set; }
    public string PriorityName { get; set; } = string.Empty;
    public string PriorityColor { get; set; } = string.Empty;

    public Guid? AssignedToId { get; set; }
    public string? AssignedToUserName { get; set; }

    public int DisplayOrder { get; set; }
    public DateTime? DueDate { get; set; }

    public int CommentCount { get; set; }
    public int AttachmentCount { get; set; }
}