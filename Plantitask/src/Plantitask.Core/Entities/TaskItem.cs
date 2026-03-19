using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Common;
using Plantitask.Core.Entities.Lookups;
using Plantitask.Core.Enums;

namespace Plantitask.Core.Entities
{
    public class TaskItem : BaseEntity
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public int StatusId { get; set; }
        public int PriorityId { get; set; }

        public User Creator { get; set; } = null!;
        public Guid GroupId { get; set; }
        public Guid? AssignedToId { get; set; }
        public TaskStatusLookup Status { get; set; } = null!;
        public TaskPriorityLookup Priority { get; set; } = null!;
        public DateTime? DueDate { get; set; }
        public DateTime? CompletedAt { get; set; }

        public int DisplayOrder { get; set; } = 0;


        [Timestamp]
        public uint RowVersion { get; set; }

        public virtual Group Group { get; set; } = null!;
        public virtual User? AssignedTo { get; set; }
        public virtual ICollection<TaskAttachment> Attachments { get; set; } = new List<TaskAttachment>();
        public virtual ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();
    }
}
