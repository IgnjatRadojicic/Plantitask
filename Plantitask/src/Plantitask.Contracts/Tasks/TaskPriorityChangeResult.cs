using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.DTO.Tasks
{
    public class TaskPriorityChangeResult
    {
        public TaskDto Task { get; set; } = null!;
        public string OldPriority { get; set; } = string.Empty;
        public string NewPriority { get; set; } = string.Empty;
     }
}
