using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManagement.Api.Interfaces;
using TaskManagement.Core.DTO.Comments;
using TaskManagement.Core.Interfaces;

namespace TaskManagement.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/tasks/{taskId}/comments")]
public class CommentsController : BaseApiController
{
    private readonly ICommentService _commentService;
    private readonly INotificationService _notificationService;
    private readonly INotificationBroadcaster _notificationBroadcaster;
    private readonly ITaskService _taskService;
    private readonly IAuditService _auditService;
    private readonly ILogger<CommentsController> _logger;

    public CommentsController(
        ICommentService commentService,
        IAuditService auditService,
        ITaskService taskService,
        INotificationService notificationService,
        INotificationBroadcaster notificationBroadcaster,
        ILogger<CommentsController> logger)
    {
        _commentService = commentService;
        _auditService = auditService;
        _notificationBroadcaster = notificationBroadcaster;
        _notificationService = notificationService;
        _taskService = taskService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(CommentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(Guid taskId, [FromBody] CreateCommentDto createCommentDto)
    {
        try
        {
            var userId = GetUserId();
            var comment = await _commentService.AddCommentAsync(taskId, createCommentDto, userId);

            await LogAuditAsync(
                _auditService,
                entityType: "TaskComment",
                entityId: comment.Id,
                action: "Created",
                propertyName: "Content",
                newValue: comment.Content);

            var task = await _taskService.GetTaskByIdAsync(taskId, userId);

            await _notificationService.NotifyTaskCommentAddedAsync(task.GroupId, task, comment);

            var usersToNotify = new List<Guid>();
            if (task.CreatedBy != comment.UserId)
            {
                usersToNotify.Add(task.CreatedBy);
            }
            if (task.AssignedToId.HasValue && task.AssignedToId.Value != comment.UserId)
            {
                usersToNotify.Add(task.AssignedToId.Value);
            }

            foreach (var userToNotify in usersToNotify.Distinct())
            {
                var notifications = await _notificationService.GetUserNotificationsAsync(
                    userToNotify,
                    unreadOnly: true);
                var notification = notifications.FirstOrDefault();

                if (notification != null)
                {
                    await _notificationBroadcaster.BroadcastNotificationAsync(notification);
                }
            }

            return CreatedAtAction(nameof(GetTaskComments), new { taskId }, comment);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding comment to task {TaskId}", taskId);
            return StatusCode(500, new { message = "An error occurred while adding the comment" });
        }
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<CommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaskComments(Guid taskId)
    {
        try
        {
            var userId = GetUserId();
            var comments = await _commentService.GetTaskCommentsAsync(taskId, userId);

            return Ok(comments);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting comments for task {TaskId}", taskId);
            return StatusCode(500, new { message = "An error occurred while retrieving comments" });
        }
    }

    [HttpPut("{commentId}")]
    [ProducesResponseType(typeof(CommentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateComment(Guid taskId, Guid commentId, [FromBody] UpdateCommentDto updateCommentDto)
    {
        try
        {
            var userId = GetUserId();
            var comment = await _commentService.UpdateCommentAsync(commentId, updateCommentDto, userId);

            await LogAuditAsync(
                _auditService,
                entityType: "TaskComment",
                entityId: commentId,
                action: "Updated",
                propertyName: "Content",
                newValue: comment.Content);

            return Ok(comment);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating comment {CommentId}", commentId);
            return StatusCode(500, new { message = "An error occurred while updating the comment" });
        }
    }

    [HttpDelete("{commentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteComment(Guid taskId, Guid commentId)
    {
        try
        {
            var userId = GetUserId();
            await _commentService.DeleteCommentAsync(commentId, userId);

            await LogAuditAsync(
                _auditService,
                entityType: "TaskComment",
                entityId: commentId,
                action: "Deleted");

            return Ok(new { message = "Comment deleted successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting comment {CommentId}", commentId);
            return StatusCode(500, new { message = "An error occurred while deleting the comment" });
        }
    }
}