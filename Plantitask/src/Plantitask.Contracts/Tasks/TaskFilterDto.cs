using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.DTO.Tasks
{
    public class TaskFilterDto
    {
        public int? StatusId { get; set; }
        public int? PriorityId { get; set; }
        public Guid? AssignedToUserId { get; set; }
        public bool? IsOverDue { get; set; }
        public string? SearchTerm { get; set; }
    }
}
