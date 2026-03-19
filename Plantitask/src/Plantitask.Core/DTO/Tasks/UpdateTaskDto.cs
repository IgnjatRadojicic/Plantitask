using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.DTO.Tasks
{
    public class UpdateTaskDto
    {
        [StringLength(200, MinimumLength = 3)]
        public string? Title { get; set; }

        [StringLength(2000)]
        public string? Description { get; set; }

        [Range(1, 4)]
        public int? PriorityId { get; set; }

        public DateTime? DueDate { get; set; }
    }
}
