using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

using Plantitask.Core.Common;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Web.Helpers;
namespace Plantitask.Web.Services;

public class TaskService : BaseApiService, ITaskService
{
    public TaskService(HttpClient http) : base(http) { }

    public Task<ServiceResult<TaskDto>> CreateTaskAsync(Guid groupId, CreateTaskDto model)
        => PostAsync<TaskDto>($"api/task/groups/{groupId}", model);

    public Task<ServiceResult<List<TaskDto>>> GetGroupTasksAsync(Guid groupId, TaskFilterDto? filter = null)
    {
        var qs = filter?.ToQueryString() ?? string.Empty;
        return GetAsync<List<TaskDto>>($"api/task/groups/{groupId}{qs}");
    }

    public Task<ServiceResult<TaskDto>> GetTaskByIdAsync(Guid taskId)
        => GetAsync<TaskDto>($"api/task/{taskId}");

    public Task<ServiceResult<TaskDto>> UpdateTaskAsync(Guid taskId, UpdateTaskDto model)
        => PutAsync<TaskDto>($"api/task/{taskId}", model);

    public Task<ServiceResult<TaskStatusChangeResult>> ChangeStatusAsync(Guid taskId, ChangeTaskStatusDto model)
        => PutAsync<TaskStatusChangeResult>($"api/task/{taskId}/status", model);

    public Task<ServiceResult<TaskPriorityChangeResult>> ChangePriorityAsync(Guid taskId, int newPriorityId)
        => PutAsync<TaskPriorityChangeResult>($"api/task/{taskId}/priority", newPriorityId);

    public Task<ServiceResult<bool>> AssignTaskAsync(Guid taskId, AssignTaskDto model)
        => PostAsync<bool>($"api/task/{taskId}/assign", model);

    public Task<ServiceResult<bool>> UnassignTaskAsync(Guid taskId)
        => PostAsync<bool>($"api/task/{taskId}/unassign", new { });

    public Task<ServiceResult<bool>> DeleteTaskAsync(Guid taskId)
        => DeleteAsync<bool>($"api/task/{taskId}");
}