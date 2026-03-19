using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.Entities.Lookups
{
    public class TaskPriorityLookup
    {
        public int Id { get; set; } 
        public string Name { get; set; } = string.Empty; 
        public string? Description { get; set; }
        public string? Color { get; set; }  
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;

        
        public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
    }
}
