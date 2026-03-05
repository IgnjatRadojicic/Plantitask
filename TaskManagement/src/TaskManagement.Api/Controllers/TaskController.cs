using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagement.Api.Interfaces;
using TaskManagement.Core.DTO.Tasks;
using TaskManagement.Core.Interfaces;
namespace TaskManagement.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class TaskController : BaseApiController
    {
        private readonly ITaskService _taskService;
        private readonly IAuditService _auditService;
        private readonly ILogger<TaskController> _logger;
        private readonly INotificationService _notificationService;
        private readonly INotificationBroadcaster _notificationBroadcaster;

        public TaskController(
            ITaskService taskService,
            IAuditService auditService,
            INotificationBroadcaster notificationBroadcaster,
            INotificationService notificationService,
        ILogger<TaskController> logger)
        {
            _taskService = taskService;
            _notificationBroadcaster = notificationBroadcaster;
            _notificationService = notificationService;
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
                var task = await _taskService.CreateTaskAsync(groupId, createTaskDto, userId);

                await LogAuditAsync(
                    _auditService,
                    entityType: "TaskItem",
                    entityId: task.Id,
                    action: "Created",
                    groupId: groupId);


                var notification = await _notificationService.NotifyTaskCreatedAsync(task.AssignedToId.Value, task);

                if (notification != null)
                {
                    await _notificationBroadcaster.BroadcastNotificationAsync(notification);
                }

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

                var tasks = await _taskService.GetGroupTasksAsync(groupId, filter, userId);

                return Ok(tasks);
        }

        [HttpGet("{taskId}")]
        [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTaskById(Guid taskId)
        {
                var userId = GetUserId();
                var task = await _taskService.GetTaskByIdAsync(taskId, userId);

                return Ok(task);

        }

        [HttpPut("{taskId}")]
        [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UpdateTask(Guid taskId, [FromBody] UpdateTaskDto updateTaskDto)
        {
                var userId = GetUserId();
                var task = await _taskService.UpdateTaskAsync(taskId, updateTaskDto, userId);

                await LogAuditAsync(
                    _auditService,
                    entityType: "TaskItem",
                    entityId: taskId,
                    action: "Updated",
                    groupId: task.GroupId);

                var notification = await _notificationService.NotifyTaskUpdatedAsync(task.GroupId, task);

                if (notification != null)
                {
                    await _notificationBroadcaster.BroadcastNotificationAsync(notification);
                }


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
                var task = await _taskService.ChangeTaskStatusAsync(taskId, statusDto, userId);

                await LogAuditAsync(
                    _auditService,
                    entityType: "TaskItem",
                    entityId: taskId,
                    action: "StatusChanged",
                    propertyName: "StatusId",
                    oldValue: task.OldStatus,
                    newValue: task.NewStatus);

                var notifications = await _notificationService.NotifyTaskStatusChangedAsync(
                    task.Task.GroupId,
                    task.Task,
                    task.OldStatus,
                    task.NewStatus);

                foreach (var notification in notifications)
                {
                    await _notificationBroadcaster.BroadcastNotificationAsync(notification);
                }

                return Ok(task);
            
        }

        [HttpPut("{taskId}/priority")]
        [ProducesResponseType(typeof(TaskDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ChangeTaskPriority(Guid taskId, [FromBody] int newPriorityId)
        {
                var userId = GetUserId();
                var task = await _taskService.ChangeTaskPriorityAsync(taskId, newPriorityId, userId);

                await LogAuditAsync(
                    _auditService,
                    entityType: "TaskItem",
                    entityId: taskId,
                    action: "PriorityChanged",
                    groupId: task.Task.GroupId,
                    propertyName: "Priority",
                    oldValue: task.OldPriority,
                    newValue: task.NewPriority);

                if (task.Task.AssignedToId.HasValue)
                {
                    var notification = await _notificationService.NotifyTaskPriorityChangedAsync(
                        task.Task.GroupId,
                        task.Task,
                        task.OldPriority,
                        task.NewPriority);

                    if (notification != null)
                    {
                        await _notificationBroadcaster.BroadcastNotificationAsync(notification);
                    }
                }

                return Ok(task);
        }

        [HttpPost("{taskId}/assign")]
        public async Task<IActionResult> AssignTask(Guid taskId, [FromBody] AssignTaskDto assignDto)
        {
                var userId = GetUserId();

                await _taskService.AssignTaskAsync(taskId, assignDto, userId);
                var task = await _taskService.GetTaskByIdAsync(taskId, userId);

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

            await _notificationService.TrySendTaskAssignmentEmailAsync(assignDto.UserId, task.Title, task.GroupName, task.CreatedByUserName);

                return Ok(new { message = "Task assigned successfully" });
            
        }

        [HttpPost("{taskId}/unassign")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> UnassignTask(Guid taskId)
        {
                var userId = GetUserId();
                var task = await _taskService.GetTaskByIdAsync(taskId, userId);

                await _taskService.UnassignTaskAsync(taskId, userId);

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
                var task = await _taskService.GetTaskByIdAsync(taskId, userId);

                await _taskService.DeleteTaskAsync(taskId, userId);

                await LogAuditAsync(
                    _auditService,
                    entityType: "TaskItem",
                    entityId: taskId,
                    action: "Deleted",
                    groupId: task.GroupId);

                return Ok(new { message = "Task deleted successfully" });
            

        }
    }

}

    