using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace Plantitask.Core.DTO.Groups
{
    public class CreateGroupDto
    {
        [Required(ErrorMessage = "Group name is required")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Group name must be between 3 and 100 characters")]
        public string Name { get; set; } = string.Empty;

        [StringLength(24, MinimumLength = 8, ErrorMessage = "Password must atleast be 8 characters long")]
        public string? Password { get; set; }

    }
}
