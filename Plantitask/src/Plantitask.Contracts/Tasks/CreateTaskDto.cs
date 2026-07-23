using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.DTO.Tasks
{
    public class CreateTaskDto
    {
        [Required]
        [StringLength(200, MinimumLength = 3)]
        public string Title { get; set; } = string.Empty;

        [StringLength(2000)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [Range(1, 4)]
        public int PriorityId { get; set; } = 2;

        public DateTime? DueDate { get; set; }

        public Guid? AssignedToUserId { get; set; }
    }
}
