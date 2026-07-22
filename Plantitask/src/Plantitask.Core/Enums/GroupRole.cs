using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.Enums
{
    public enum GroupRole
    {
        // Value IS the permission rank and IS the GroupRoleLookup.Id primary key.
        // Gaps (10, 40, 60, ...) are intentionally free for future roles like Viewer.
        Member = 25,
        TeamLead = 50,
        Manager = 75,
        Owner = 100,
    }
}
