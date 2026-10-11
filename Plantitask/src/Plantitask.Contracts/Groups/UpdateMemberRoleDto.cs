using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Contracts.Enums;

namespace Plantitask.Contracts.DTO.Groups
{
    public class UpdateMemberRoleDto
    {
        [Required]
        public GroupRole Role { get; set; }
    }
}
