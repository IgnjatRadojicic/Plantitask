using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.DTO.Tasks
{
    public class TaskDto
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
