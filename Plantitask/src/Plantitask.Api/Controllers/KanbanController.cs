using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plantitask.Api.Extensions;
using Plantitask.Core.DTO.Kanban;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Controllers
{
    [Authorize]
    [ApiController]
    [EnableRateLimiting("general")]
    [Route("api/[controller]")]
    public class KanbanController : BaseApiController
    {
        private readonly ITaskService _taskService;
        private readonly IAuditService _auditService;
        private readonly IKanbanBroadcaster _kanbanBroadcaster;
        private readonly ITreeProgressBroadcaster _treeBroadcaster;

        public KanbanController(ITaskService taskService, 
            IAuditService auditService,
            IKanbanBroadcaster kanbanBroadcaster,
            ITreeProgressBroadcaster treeBroadcaster
            )
        {
            _taskService = taskService;
            _auditService = auditService;
            _kanbanBroadcaster = kanbanBroadcaster;
            _treeBroadcaster = treeBroadcaster;
        }

        [HttpGet("{groupId}")]
        [ProducesResponseType(typeof(KanbanBoardDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetBoard(Guid groupId)
        {
            var userId = GetUserId();
            var result = await _taskService.GetKanbanBoardAsync(groupId, userId);
            return result.ToActionResult();
        }

        [HttpPut("{taskId}/move")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> MoveTask(Guid taskId, [FromBody] MoveTaskDto moveDto)
        {
            var userId = GetUserId();

            var taskResult = await _taskService.GetTaskByIdAsync(taskId, userId);
            if (taskResult.IsFailure)
                return taskResult.ToActionResult();

            var task = taskResult.Value!;
            var oldStatusId = task.StatusId;

            var moveResult = await _taskService.MoveTaskAsync(taskId, moveDto, userId);
            if (moveResult.IsFailure)
                return moveResult.ToActionResult();

            await LogAuditAsync(
                _auditService,
                entityType: "TaskItem",
                entityId: taskId,
                action: "Moved",
                propertyName: "StatusId",
                newValue: moveDto.NewStatusId.ToString());

            await _kanbanBroadcaster.BroadcastTaskMovedAsync(task.GroupId, taskId, oldStatusId, moveDto, userId);
            await _treeBroadcaster.BroadcastTreeUpdateAsync(task.GroupId);

            return Ok(new { message = "Task moved successfully" });
        }
    }
}