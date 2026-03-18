using TaskManagement.Web.Models;

namespace TaskManagement.Web.Interfaces;

public interface IKanbanService
{
    Task<ServiceResult<KanbanBoardDto>> GetBoardAsync(Guid groupId);
    Task<ServiceResult<MessageResponse>> MoveTaskAsync(Guid taskId, MoveTaskDto model);
}