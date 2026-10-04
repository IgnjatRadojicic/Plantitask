using System;

namespace Plantitask.Core.DTO.Tasks
{
    public class TaskReminder
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int StatusId { get; set; }
        public DateOnly? DueDate { get; set; }
        public DateTime? DueAt { get; set; }

        public Guid? AssignedToId { get; set; }
        public string? AssigneeEmail { get; set; }
        public string? AssigneeName { get; set; }
    }
}
