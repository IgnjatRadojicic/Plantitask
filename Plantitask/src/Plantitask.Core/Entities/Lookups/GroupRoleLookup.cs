using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.Entities.Lookups
{
    public class GroupRoleLookup
    {
        public int Id { get; set; }  
        public string Name { get; set; } = string.Empty; 
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int PermissionLevel { get; set; } 
        public bool IsActive { get; set; } = true;


        public ICollection<GroupMember> GroupMembers { get; set; } = new List<GroupMember>();
    }
}
