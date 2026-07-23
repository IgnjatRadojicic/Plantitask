using System;

namespace Plantitask.Core.DTO.Kanban
{
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
}
