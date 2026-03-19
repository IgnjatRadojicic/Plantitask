using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

namespace Plantitask.Web.Services;

public class TaskService : BaseApiService, ITaskService
{
    public TaskService(HttpClient http) : base(http) { }

    public Task<ServiceResult<TaskItemDto>> CreateTaskAsync(Guid groupId, CreateTaskDto model)
        => PostAsync<TaskItemDto>($"api/task/groups/{groupId}", model);

    public Task<ServiceResult<List<TaskItemDto>>> GetGroupTasksAsync(Guid groupId, TaskFilterDto? filter = null)
    {
        var qs = filter?.ToQueryString() ?? string.Empty;
        return GetAsync<List<TaskItemDto>>($"api/task/groups/{groupId}{qs}");
    }

    public Task<ServiceResult<TaskItemDto>> GetTaskByIdAsync(Guid taskId)
        => GetAsync<TaskItemDto>($"api/task/{taskId}");

    public Task<ServiceResult<TaskItemDto>> UpdateTaskAsync(Guid taskId, UpdateTaskDto model)
        => PutAsync<TaskItemDto>($"api/task/{taskId}", model);

    public Task<ServiceResult<TaskStatusChangeResultDto>> ChangeStatusAsync(Guid taskId, ChangeTaskStatusDto model)
        => PutAsync<TaskStatusChangeResultDto>($"api/task/{taskId}/status", model);

    public Task<ServiceResult<TaskPriorityChangeResultDto>> ChangePriorityAsync(Guid taskId, int newPriorityId)
        => PutAsync<TaskPriorityChangeResultDto>($"api/task/{taskId}/priority", newPriorityId);

    public Task<ServiceResult<bool>> AssignTaskAsync(Guid taskId, AssignTaskDto model)
        => PostAsync<bool>($"api/task/{taskId}/assign", model);

    public Task<ServiceResult<bool>> UnassignTaskAsync(Guid taskId)
        => PostAsync<bool>($"api/task/{taskId}/unassign", new { });

    public Task<ServiceResult<bool>> DeleteTaskAsync(Guid taskId)
        => DeleteAsync<bool>($"api/task/{taskId}");
}