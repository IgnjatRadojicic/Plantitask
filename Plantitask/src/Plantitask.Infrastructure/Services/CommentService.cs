using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.Enums;
using Plantitask.Core.DTO.Comments;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Projections;

namespace Plantitask.Infrastructure.Services;

/// <summary>
/// Comments on tasks. Reads and writes are membership-gated through the task's group;
/// editing is author-only while deleting extends to Managers and above.
/// </summary>
public class CommentService : ICommentService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<CommentService> _logger;
    private readonly IGroupService _groupService;

    public CommentService(
        IApplicationDbContext context,
        ILogger<CommentService> logger,
        IGroupService groupService)
    {
        _groupService = groupService;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Adds a comment to a task the caller can see. The task lookup uses the nullable-Guid
    /// projection so "task missing" and "task found" are distinguishable without loading the row.
    /// </summary>
    public async Task<Result<CommentDto>> AddCommentAsync(Guid taskId, CreateCommentDto createCommentDto, Guid userId)
    {
        _logger.LogInformation("User {UserId} adding comment to task {TaskId}", userId, taskId);

        var groupId = await _context.Tasks
             .Where(t => t.Id == taskId)
             .Select(t => (Guid?)t.GroupId)
             .FirstOrDefaultAsync();

        if (groupId == null)
            return Error.NotFound("Task not found");

        var isMember = await _groupService.IsUserMemberAsync(groupId.Value, userId);

        if (!isMember)
            return Error.Forbidden("You must be a member of the group to comment on tasks");

        var comment = new TaskComment
        {
            TaskId = taskId,
            Content = createCommentDto.Content,
            CreatedBy = userId, 
        };

        _context.TaskComments.Add(comment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Comment {CommentId} added to task {TaskId} by user {UserId}",
            comment.Id, taskId, userId);

        return await GetCommentByIdInternalAsync(comment.Id);
    }

    /// <summary>
    /// A task's comments for a group member, newest first, through the shared projection and
    /// pagination contract.
    /// </summary>
    public async Task<Result<PaginatedList<CommentDto>>> GetTaskCommentsAsync(Guid taskId, Guid userId, int pageNumber = 1, int pageSize = 20)
    {
        var groupId = await _context.Tasks
            .Where(t => t.Id == taskId)
            .Select(t => (Guid?)t.GroupId)
            .FirstOrDefaultAsync();

        if (groupId == null)
            return Error.NotFound("Task not found");

        var isMember = await _groupService.IsUserMemberAsync(groupId.Value, userId);

        if (!isMember)
            return Error.Forbidden("You must be a member of the group to view task comments");

        var query = _context.TaskComments
            .Where(tc => tc.TaskId == taskId)
            .OrderByDescending(tc => tc.CreatedAt);

        return await query
            .Select(CommentProjections.ToDto)
            .ToPaginatedListAsync(pageNumber, pageSize);
    }

    /// <summary>
    /// Edits a comment. Only the author may edit, and they must still be a member of the group -
    /// leaving the group ends your ability to touch what you wrote there.
    /// </summary>
    public async Task<Result<CommentDto>> UpdateCommentAsync(Guid commentId, UpdateCommentDto updateCommentDto, Guid userId)
    {
        var row = await _context.TaskComments
            .Where(tc => tc.Id == commentId)
            .Select(tc => new { Comment = tc, groupId = tc.Task.GroupId })
            .FirstOrDefaultAsync();

        if (row == null)
            return Error.NotFound("Comment not found");

        var comment = row.Comment;

        if (comment.CreatedBy != userId)
            return Error.Forbidden("You can only edit your own comments");

        if (!await _groupService.IsUserMemberAsync(row.groupId, userId))
            return Error.Forbidden("You must be a member of the group");

        comment.Content = updateCommentDto.Content;
        comment.UpdatedBy = userId;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Comment {CommentId} updated by user {UserId}", commentId, userId);

        return await GetCommentByIdInternalAsync(commentId);
    }

    /// <summary>
    /// Soft-deletes a comment. Membership comes first, then the rule: the author or a Manager
    /// and above. This ordering is the reference shape for "creator may X" checks.
    /// </summary>
    public async Task<Result> DeleteCommentAsync(Guid commentId, Guid userId)
    {
        var row = await _context.TaskComments
            .Where(tc => tc.Id == commentId)
            .Select(tc => new { Comment = tc, groupId = tc.Task.GroupId })
            .FirstOrDefaultAsync();

        if (row == null)
            return Error.NotFound("Comment not found");

        var comment = row.Comment;

        var callerRole = await _groupService.GetUserRoleAsync(row.groupId, userId);

        if (callerRole == null)
            return Error.Forbidden("You must be a member of the group");

        var canDelete = comment.CreatedBy == userId || callerRole >= GroupRole.Manager;

        if (!canDelete)
            return Error.Forbidden("You can only delete your own comments or you must be a Manager or Owner");

        comment.IsDeleted = true;
        comment.DeletedAt = DateTime.UtcNow;
        comment.DeletedBy = userId;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Comment {CommentId} deleted by user {UserId}", commentId, userId);

        return Result.Success();
    }

    /// <summary>
    /// Re-reads one comment through the shared projection after a write - the DTO carries the
    /// author's name, which a freshly saved entity does not have loaded.
    /// </summary>
    private async Task<Result<CommentDto>> GetCommentByIdInternalAsync(Guid commentId)
    {
        var dto = await _context.TaskComments
            .Where(tc => tc.Id == commentId)
            .Select(CommentProjections.ToDto)
            .FirstOrDefaultAsync();

        return dto is null ? Error.NotFound("Comment not found") : dto;
    }
}