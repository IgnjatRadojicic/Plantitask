using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.DTO.Kanban;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Tasks;

namespace Plantitask.Core.Interfaces
{
    public interface ITaskService
    {
        Task<Result<TaskDto>> CreateTaskAsync(Guid groupId, CreateTaskDto createTaskDto, Guid userId);
        Task<Result<List<TaskDto>>> GetGroupTasksAsync(Guid groupId, TaskFilterDto? filter, Guid userId);
        Task<Result<TaskDto>> GetTaskByIdAsync(Guid taskId, Guid userId);
        Task<Result<TaskDto>> UpdateTaskAsync(Guid taskId, UpdateTaskDto updateTaskDto, Guid userId);
        Task<Result<TaskStatusChangeResult>> ChangeTaskStatusAsync(Guid taskId, ChangeTaskStatusDto statusDto, Guid userId);
        Task<Result<TaskPriorityChangeResult>> ChangeTaskPriorityAsync(Guid taskId, int newPriorityId, Guid userId);
        Task<Result> AssignTaskAsync(Guid taskId, AssignTaskDto assignDto, Guid userId);
        Task<Result> UnassignTaskAsync(Guid taskId, Guid userId);
        Task<Result> DeleteTaskAsync(Guid taskId, Guid userId);
        Task<Result<List<Guid>>> GetTaskGroupMembersAsync(Guid taskId, Guid userId);
        Task<Result<KanbanBoardDto>> GetKanbanBoardAsync(Guid groupId, Guid userId);
        Task<Result> MoveTaskAsync(Guid taskId, MoveTaskDto moveDto, Guid userId);
    }
}
