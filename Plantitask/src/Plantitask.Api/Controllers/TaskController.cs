using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plantitask.Api.Extensions;
using Plantitask.Api.Interfaces;
using Plantitask.Api.Services;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("general")]
    public class TaskController : BaseApiController
    {
        private readonly ITaskService _taskService;
        private readonly IAuditService _auditService;
        private readonly ILogger<TaskController> _logger;
        private readonly INotificationService _notificationService;
        private readonly INotificationBroadcaster _notificationBroadcaster;
        private readonly ITreeProgressBroadcaster _treeBroadcaster;
        private readonly IKanbanBroadcaster _kanbanBroadcaster;


        public TaskController(
            ITaskService taskService,
            IAuditService auditService,
            INotificationBroadcaster notificationBroadcaster,
            INotificationService notificationService,
            ITreeProgressBroadcaster treeBroadcaster,
            IKanbanBroadcaster kanbanBroadcaster,
            ILogger<TaskController> logger)
        {
            _taskService = taskService;
            _notificationBroadcaster = notificationBroadcaster;
            _notificationService = notificationService;
            _treeBroadcaster = treeBroadcaster;
            _kanbanBroadcaster = kanbanBroadcaster;
            _auditService = auditService;
            _logger = logger;
        }

        [HttpPost("groups/{groupId}")]
        [ProducesResponseType(typeof(TaskDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateTask(Guid groupId, [FromBody] CreateTaskDto createTaskDto)
        {
            var userId = GetUserId();
            var result = await _taskService.CreateTaskAsync(groupId, createTaskDto, userId);

            if (result.IsFailure)
                return result.ToActionResult();

            var task = result.Value!;

            await LogAuditAsync(
                _auditService,
                entityType: "TaskItem",
                entityId: task.Id,
                action: "Created",
                groupId: groupId);

            if (task.AssignedToId.HasValue)
            {
                var notification = await _notificationService.NotifyTaskCreatedAsync(task.AssignedToId.Value, task);
                if (notification != null)
                    await _notificationBroadcaster.BroadcastNotificationAsync(notification);
            }

            await _kanbanBroadcaster.BroadcastTaskCreatedAsync(groupId, task.Id, task.StatusId, userId);
            return CreatedAtAction(
                nameof(GetTaskById),
                new { taskId = task.Id },
                task);
        }

        [HttpGet("groups/{groupId}")]
        [ProducesResponseType(typeof(List<TaskDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetGroupTasks(
            Guid groupId,
            [FromQuery] int? statusId = null,
            [FromQuery] int? priorityId = null,
            [FromQuery] Guid? assignedToUserId = null,
            [FromQuery] bool? isOverDue = null,
            [FromQuery] string? searchTerm = null)
        {
            var userId = GetUserId();

            var filter = new TaskFilterDto
            {
                StatusId = statusId,
                PriorityId = priorityId,
                AssignedToUserId = assignedToUserId,
                IsOverDue = isOverDue,
                SearchTerm = searchTerm
            };

            var result = await _taskService.GetGroupTasksAsync(groupId, filter, userId);
            return result.ToActionResult();
        }

        [HttpGet("{taskId}")]
        [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTaskById(Guid taskId)
        {
            var userId = GetUserId();
            var result = await _taskService.GetTaskByIdAsync(taskId, userId);
            return result.ToActionResult();
        }

        [HttpPut("{taskId}")]
        [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateTask(Guid taskId, [FromBody] UpdateTaskDto updateTaskDto)
        {
            var userId = GetUserId();
            var result = await _taskService.UpdateTaskAsync(taskId, updateTaskDto, userId);

            if (result.IsFailure)
                return result.ToActionResult();

            var task = result.Value!;

            await LogAuditAsync(
                _auditService,
                entityType: "TaskItem",
                entityId: taskId,
                action: "Updated",
                groupId: task.GroupId);

            var notification = await _notificationService.NotifyTaskUpdatedAsync(task.GroupId, task);
            if (notification != null)
                await _notificationBroadcaster.BroadcastNotificationAsync(notification);

            await _kanbanBroadcaster.BroadcastTaskUpdatedAsync(task.GroupId, taskId, userId);
            return Ok(task);

        }

        [HttpPut("{taskId}/status")]
        [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ChangeTaskStatus(Guid taskId, [FromBody] ChangeTaskStatusDto statusDto)
        {
            var userId = GetUserId();
            var result = await _taskService.ChangeTaskStatusAsync(taskId, statusDto, userId);

            if (result.IsFailure)
                return result.ToActionResult();

            var statusChange = result.Value!;

            await LogAuditAsync(
                _auditService,
                entityType: "TaskItem",
                entityId: taskId,
                action: "StatusChanged",
                propertyName: "StatusId",
                oldValue: statusChange.OldStatus,
                newValue: statusChange.NewStatus);

            var notifications = await _notificationService.NotifyTaskStatusChangedAsync(
                statusChange.Task.GroupId,
                statusChange.Task,
                statusChange.OldStatus,
                statusChange.NewStatus);

            foreach (var notification in notifications)
                await _notificationBroadcaster.BroadcastNotificationAsync(notification);

            await _treeBroadcaster.BroadcastTreeUpdateAsync(statusChange.Task.GroupId);

            return Ok(statusChange);
        }

        [HttpPut("{taskId}/priority")]
        [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ChangeTaskPriority(Guid taskId, [FromBody] int newPriorityId)
        {
            var userId = GetUserId();
            var result = await _taskService.ChangeTaskPriorityAsync(taskId, newPriorityId, userId);

            if (result.IsFailure)
                return result.ToActionResult();

            var priorityChange = result.Value!;

            await LogAuditAsync(
                _auditService,
                entityType: "TaskItem",
                entityId: taskId,
                action: "PriorityChanged",
                groupId: priorityChange.Task.GroupId,
                propertyName: "Priority",
                oldValue: priorityChange.OldPriority,
                newValue: priorityChange.NewPriority);

            if (priorityChange.Task.AssignedToId.HasValue)
            {
                var notification = await _notificationService.NotifyTaskPriorityChangedAsync(
                    priorityChange.Task.GroupId,
                    priorityChange.Task,
                    priorityChange.OldPriority,
                    priorityChange.NewPriority);

                if (notification != null)
                    await _notificationBroadcaster.BroadcastNotificationAsync(notification);
            }

            return Ok(priorityChange);
        }

        [HttpPost("{taskId}/assign")]
        public async Task<IActionResult> AssignTask(Guid taskId, [FromBody] AssignTaskDto assignDto)
        {
            var userId = GetUserId();

            var assignResult = await _taskService.AssignTaskAsync(taskId, assignDto, userId);
            if (assignResult.IsFailure)
                return assignResult.ToActionResult();

            var taskResult = await _taskService.GetTaskByIdAsync(taskId, userId);
            if (taskResult.IsFailure)
                return taskResult.ToActionResult();

            var task = taskResult.Value!;

            await LogAuditAsync(
                _auditService,
                entityType: "TaskItem",
                entityId: taskId,
                action: "Assigned",
                groupId: task.GroupId,
                propertyName: "AssignedTo",
                newValue: task.AssignedToUserName);

            var notification = await _notificationService.NotifyTaskAssignedAsync(assignDto.UserId, task);

            _logger.LogInformation("NotifyTaskAssignedAsync returned: {NotificationId}", notification?.Id);

            if (notification != null)
            {
                await _notificationBroadcaster.BroadcastNotificationAsync(notification);
                _logger.LogInformation("Broadcast complete");
            }

            await _notificationService.TrySendTaskAssignmentEmailAsync(
                assignDto.UserId, task.Title, task.GroupName, task.CreatedByUserName);

            return Ok(new { message = "Task assigned successfully" });
        }

        [HttpPost("{taskId}/unassign")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UnassignTask(Guid taskId)
        {
            var userId = GetUserId();

            var taskResult = await _taskService.GetTaskByIdAsync(taskId, userId);
            if (taskResult.IsFailure)
                return taskResult.ToActionResult();

            var task = taskResult.Value!;

            var unassignResult = await _taskService.UnassignTaskAsync(taskId, userId);
            if (unassignResult.IsFailure)
                return unassignResult.ToActionResult();

            await LogAuditAsync(
                _auditService,
                entityType: "TaskItem",
                entityId: taskId,
                action: "Unassigned",
                groupId: task.GroupId,
                propertyName: "AssignedTo",
                oldValue: task.AssignedToUserName);

            return Ok(new { message = "Task unassigned successfully" });
        }

        [HttpDelete("{taskId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteTask(Guid taskId)
        {
            var userId = GetUserId();

            var taskResult = await _taskService.GetTaskByIdAsync(taskId, userId);
            if (taskResult.IsFailure)
                return taskResult.ToActionResult();

            var task = taskResult.Value!;

            var deleteResult = await _taskService.DeleteTaskAsync(taskId, userId);
            if (deleteResult.IsFailure)
                return deleteResult.ToActionResult();

            await LogAuditAsync(
                _auditService,
                entityType: "TaskItem",
                entityId: taskId,
                action: "Deleted",
                groupId: task.GroupId);

            await _kanbanBroadcaster.BroadcastTaskDeletedAsync(task.GroupId, taskId, task.StatusId, userId);

            return Ok(new { message = "Task deleted successfully" });
        }
    }
}