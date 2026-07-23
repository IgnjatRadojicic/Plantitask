using Plantitask.Web.Models;

using Plantitask.Core.Common;
using Plantitask.Core.DTO.Tasks;
namespace Plantitask.Web.Interfaces;

public interface ITaskService
{
    Task<ServiceResult<TaskDto>> CreateTaskAsync(Guid groupId, CreateTaskDto model);
    Task<ServiceResult<List<TaskDto>>> GetGroupTasksAsync(Guid groupId, TaskFilterDto? filter = null);
    Task<ServiceResult<TaskDto>> GetTaskByIdAsync(Guid taskId);
    Task<ServiceResult<TaskDto>> UpdateTaskAsync(Guid taskId, UpdateTaskDto model);
    Task<ServiceResult<TaskStatusChangeResult>> ChangeStatusAsync(Guid taskId, ChangeTaskStatusDto model);
    Task<ServiceResult<TaskPriorityChangeResult>> ChangePriorityAsync(Guid taskId, int newPriorityId);
    Task<ServiceResult<bool>> AssignTaskAsync(Guid taskId, AssignTaskDto model);
    Task<ServiceResult<bool>> UnassignTaskAsync(Guid taskId);
    Task<ServiceResult<bool>> DeleteTaskAsync(Guid taskId);
}