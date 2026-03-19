using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Enums;

namespace Plantitask.Core.DTO.Groups
{
    public class UpdateMemberRoleDto
    {
        [Required]
        public GroupRole Role { get; set; }
    }
}
