namespace Plantitask.Web.Models
{

    // ===== Kanban Models =====

    public class KanbanTaskCreatedEvent
    {
        public Guid TaskId { get; set; }
        public int StatusId { get; set; }
        public Guid CreatedByUserId { get; set; }
    }

    public class KanbanTaskDeletedEvent
    {
        public Guid TaskId { get; set; }
        public int StatusId { get; set; }
        public Guid DeletedByUserId { get; set; }
    }

    public class KanbanTaskUpdatedEvent
    {
        public Guid TaskId { get; set; }
        public Guid UpdatedByUserId { get; set; }
    }

    public class KanbanTaskMovedEvent
    {
        public Guid TaskId { get; set; }
        public int OldStatusId { get; set; }
        public int NewStatusId { get; set; }
        public int NewDisplayOrder { get; set; }
        public Guid MovedByUserId { get; set; }
    }

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

        public bool IsOverdue => DueDate.HasValue
            && DueDate.Value.Date < DateTime.UtcNow.Date
            && StatusId != 4;
    }

    public class KanbanColumnDto
    {
        public int StatusId { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public List<KanbanTaskDto> Tasks { get; set; } = new();
    }


    public class KanbanBoardDto
    {
        public Guid GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public List<KanbanColumnDto> Columns { get; set; } = new();
    }
}
