using Plantitask.Web.Models;

using Plantitask.Contracts.DTO.Kanban;
namespace Plantitask.Web.Interfaces;

public interface IKanbanService
{
    Task<ServiceResult<KanbanBoardDto>> GetBoardAsync(Guid groupId);
    Task<ServiceResult<MessageResponse>> MoveTaskAsync(Guid taskId, MoveTaskDto model);
}