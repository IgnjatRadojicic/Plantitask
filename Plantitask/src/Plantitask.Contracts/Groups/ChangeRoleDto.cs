using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Contracts.Enums;

namespace Plantitask.Contracts.DTO.Groups
{
    public class ChangeRoleDto
    {
        public GroupRole NewRole { get; set; }
    }
}
