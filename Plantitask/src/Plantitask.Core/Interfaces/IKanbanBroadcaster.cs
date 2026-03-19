using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.DTO.Kanban;

namespace Plantitask.Core.Interfaces
{
    public interface IKanbanBroadcaster
    {
        Task BroadcastTaskMovedAsync(Guid groupId, Guid taskId, int oldStatusId, MoveTaskDto moveDto, Guid movedByUserId);
        Task BroadcastTaskCreatedAsync(Guid groupId, Guid taskId, int statusId, Guid createdByUserId);
        Task BroadcastTaskDeletedAsync(Guid groupId, Guid taskId, int statusId, Guid deletedByUserId);
        Task BroadcastTaskUpdatedAsync(Guid groupId, Guid taskId, Guid updatedByUserId);
    }
}
