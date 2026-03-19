using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.Enums
{
    public enum NotificationType
    {
        TaskAssigned = 1,
        TaskStatusChanged = 2,
        TaskCommentAdded = 3,
        TaskDueSoon = 4,
        TaskOverdue = 5,
        GroupInvitation = 6,
        TaskPriorityChanged = 7,
        TaskUpdated = 8
    }
}
