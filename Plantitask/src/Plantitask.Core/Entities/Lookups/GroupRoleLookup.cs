using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.Entities.Lookups
{
    public class GroupRoleLookup : BaseLookupEntity
    {
        // The rank is the Id (== the GroupRole enum value). There is no separate level column.
        public ICollection<GroupMember> GroupMembers { get; set; } = new List<GroupMember>();
    }
}
