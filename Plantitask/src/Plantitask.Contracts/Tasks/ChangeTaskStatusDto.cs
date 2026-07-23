using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.DTO.Tasks
{
    public class ChangeTaskStatusDto
    {
        [Required]
        [Range(1, 4)] // 1=NotStarted, 2=InProgress, 3=UnderReview, 4=Completed
        public int NewStatusId { get; set; }
    }
}
