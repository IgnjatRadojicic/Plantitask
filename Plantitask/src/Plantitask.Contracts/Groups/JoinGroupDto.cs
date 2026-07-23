using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace Plantitask.Core.DTO.Groups
{
    public class JoinGroupDto
    {
        [Required(ErrorMessage = "Group code is required")]
        [StringLength(20, ErrorMessage = "Invalid group code")]
        public string GroupCode { get; set; } = string.Empty;
        public string? Password { get; set; }

    }
}
