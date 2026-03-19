using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plantitask.Api.Extensions;
using Plantitask.Api.Interfaces;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Comments;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Controllers;

[Authorize]
[ApiController]
[EnableRateLimiting("general")]
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
        var userId = GetUserId();
        var commentResult = await _commentService.AddCommentAsync(taskId, createCommentDto, userId);

        if (commentResult.IsFailure)
            return commentResult.ToActionResult();

        var comment = commentResult.Value!;

        await LogAuditAsync(
            _auditService,
            entityType: "TaskComment",
            entityId: comment.Id,
            action: "Created",
            propertyName: "Content",
            newValue: comment.Content);

        var taskResult = await _taskService.GetTaskByIdAsync(taskId, userId);

        if (taskResult.IsFailure)
            return taskResult.ToActionResult();

        var task = taskResult.Value!;

        var notifications = await _notificationService.NotifyTaskCommentAddedAsync(
            task.GroupId,
            task,
            comment);

        foreach (var notification in notifications)
            await _notificationBroadcaster.BroadcastNotificationAsync(notification);

        await _notificationService.TrySendCommentEmailAsync(
            task.AssignedToId ?? Guid.Empty, userId, task.Title, comment.Content);

        return CreatedAtAction(nameof(GetTaskComments), new { taskId }, comment);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaginatedList<CommentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTaskComments(
        Guid taskId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20)
    {
        var userId = GetUserId();
        var result = await _commentService.GetTaskCommentsAsync(taskId, userId, pageNumber, pageSize);
        return result.ToActionResult();
    }

    [HttpPut("{commentId}")]
    [ProducesResponseType(typeof(CommentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateComment(Guid taskId, Guid commentId, [FromBody] UpdateCommentDto updateCommentDto)
    {
        var userId = GetUserId();
        var result = await _commentService.UpdateCommentAsync(commentId, updateCommentDto, userId);

        if (result.IsFailure)
            return result.ToActionResult();

        var comment = result.Value!;

        await LogAuditAsync(
            _auditService,
            entityType: "TaskComment",
            entityId: commentId,
            action: "Updated",
            propertyName: "Content",
            newValue: comment.Content);

        return Ok(comment);
    }

    [HttpDelete("{commentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteComment(Guid taskId, Guid commentId)
    {
        var userId = GetUserId();
        var result = await _commentService.DeleteCommentAsync(commentId, userId);

        if (result.IsFailure)
            return result.ToActionResult();

        await LogAuditAsync(
            _auditService,
            entityType: "TaskComment",
            entityId: commentId,
            action: "Deleted");

        return Ok(new { message = "Comment deleted successfully" });
    }
}