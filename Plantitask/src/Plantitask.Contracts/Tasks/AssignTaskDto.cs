using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.DTO.Tasks
{
    public class AssignTaskDto
    {
        [Required]
        public Guid UserId { get; set; }
    }
}
