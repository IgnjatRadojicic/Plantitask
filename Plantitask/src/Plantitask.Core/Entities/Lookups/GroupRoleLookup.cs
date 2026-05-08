using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.Entities.Lookups
{
    public class GroupRoleLookup : BaseLookupEntity
    {
        public int PermissionLevel { get; set; } 
        public ICollection<GroupMember> GroupMembers { get; set; } = new List<GroupMember>();
    }
}
