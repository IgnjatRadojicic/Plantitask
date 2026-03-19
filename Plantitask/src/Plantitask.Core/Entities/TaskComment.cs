using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Common;

namespace Plantitask.Core.Entities
{
    public class TaskComment : BaseEntity {
        public Guid TaskId { get; set; }
        public string Content { get; set; } = string.Empty;
        public Guid UserId { get; set; }

        public TaskItem Task { get; set; } = null!;
        public User User { get; set; } = null!;
    }
}
