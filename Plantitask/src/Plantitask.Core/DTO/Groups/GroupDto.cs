using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Enums;

namespace Plantitask.Core.DTO.Groups
{
    public class GroupDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string GroupCode { get; set; } = string.Empty;
        public bool IsPasswordProtected { get; set; }
        public int MemberCount { get; set; }
        public GroupRole UserRole { get; set; }
        public DateTime JoinedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
