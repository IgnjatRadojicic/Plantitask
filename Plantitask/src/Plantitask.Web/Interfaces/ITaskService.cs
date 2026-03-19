using Plantitask.Web.Models;

namespace Plantitask.Web.Interfaces;

public interface ITaskService
{
    Task<ServiceResult<TaskItemDto>> CreateTaskAsync(Guid groupId, CreateTaskDto model);
    Task<ServiceResult<List<TaskItemDto>>> GetGroupTasksAsync(Guid groupId, TaskFilterDto? filter = null);
    Task<ServiceResult<TaskItemDto>> GetTaskByIdAsync(Guid taskId);
    Task<ServiceResult<TaskItemDto>> UpdateTaskAsync(Guid taskId, UpdateTaskDto model);
    Task<ServiceResult<TaskStatusChangeResultDto>> ChangeStatusAsync(Guid taskId, ChangeTaskStatusDto model);
    Task<ServiceResult<TaskPriorityChangeResultDto>> ChangePriorityAsync(Guid taskId, int newPriorityId);
    Task<ServiceResult<bool>> AssignTaskAsync(Guid taskId, AssignTaskDto model);
    Task<ServiceResult<bool>> UnassignTaskAsync(Guid taskId);
    Task<ServiceResult<bool>> DeleteTaskAsync(Guid taskId);
}