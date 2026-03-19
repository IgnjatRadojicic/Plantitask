using Microsoft.AspNetCore.Mvc.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.Constants;
using Plantitask.Core.DTO.Comments;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services;

public class CommentService : ICommentService
{
    private readonly IApplicationDbContext _context;
    private readonly ILogger<CommentService> _logger;

    public CommentService(
        IApplicationDbContext context,
        ILogger<CommentService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<Result<CommentDto>> AddCommentAsync(Guid taskId, CreateCommentDto createCommentDto, Guid userId)
    {
        _logger.LogInformation("User {UserId} adding comment to task {TaskId}", userId, taskId);

        var task = await _context.Tasks
            .Include(t => t.Group)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null)
            return Error.NotFound("Task not found");

        var isMember = await _context.GroupMembers
            .AnyAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

        if (!isMember)
            return Error.Forbidden("You must be a member of the group to comment on tasks");

        var comment = new TaskComment
        {
            TaskId = taskId,
            Content = createCommentDto.Content,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.TaskComments.Add(comment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Comment {CommentId} added to task {TaskId} by user {UserId}",
            comment.Id, taskId, userId);

        return await GetCommentByIdInternalAsync(comment.Id);
    }

    public async Task<Result<PaginatedList<CommentDto>>> GetTaskCommentsAsync(Guid taskId, Guid userId, int pageNumber = 1, int pageSize = 20)
    {
        var task = await _context.Tasks
            .Include(t => t.Group)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null)
            return Error.NotFound("Task not found");

        var isMember = await _context.GroupMembers
            .AnyAsync(gm => gm.GroupId == task.GroupId && gm.UserId == userId);

        if (!isMember)
            return Error.Forbidden("You must be a member of the group to view task comments");

        var query = _context.TaskComments
            .Where(tc => tc.TaskId == taskId)
            .OrderBy(tc => tc.CreatedAt);

        var totalCount = await query.CountAsync();


        var comments = await query
            .Include(tc => tc.User)
            .Skip((pageNumber - 1 ) * pageSize)
            .Take(pageSize)
            .Select(tc => new CommentDto
            {
                Id = tc.Id,
                TaskId = tc.TaskId,
                Content = tc.Content,
                UserId = tc.UserId,
                UserName = tc.User.UserName,
                CreatedAt = tc.CreatedAt,
                UpdatedAt = tc.UpdatedAt
            })
            .ToListAsync();

        return new PaginatedList<CommentDto>
        {
            Items = comments,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<Result<CommentDto>> UpdateCommentAsync(Guid commentId, UpdateCommentDto updateCommentDto, Guid userId)
    {
        var comment = await _context.TaskComments
            .Include(tc => tc.Task)
            .ThenInclude(t => t.Group)
            .FirstOrDefaultAsync(tc => tc.Id == commentId);

        if (comment == null)
            return Error.NotFound("Comment not found");

        if (comment.UserId != userId)
            return Error.Forbidden("You can only edit your own comments");

        comment.Content = updateCommentDto.Content;
        comment.UpdatedAt = DateTime.UtcNow;
        comment.UpdatedBy = userId;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Comment {CommentId} updated by user {UserId}", commentId, userId);

        return await GetCommentByIdInternalAsync(commentId);
    }

    public async Task<Result> DeleteCommentAsync(Guid commentId, Guid userId)
    {
        var comment = await _context.TaskComments
            .Include(tc => tc.Task)
            .ThenInclude(t => t.Group)
            .FirstOrDefaultAsync(tc => tc.Id == commentId);

        if (comment == null)
            return Error.NotFound("Comment not found");

        var membership = await _context.GroupMembers
            .Include(gm => gm.Role)
            .FirstOrDefaultAsync(gm => gm.GroupId == comment.Task.GroupId && gm.UserId == userId);

        if (membership == null)
            return Error.Forbidden("You must be a member of the group");

        var canDelete = comment.UserId == userId || membership.Role.PermissionLevel >= PermissionLevels.Manager;

        if (!canDelete)
            return Error.Forbidden("You can only delete your own comments or you must be a Manager or Owner");

        comment.IsDeleted = true;
        comment.DeletedAt = DateTime.UtcNow;
        comment.DeletedBy = userId;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Comment {CommentId} deleted by user {UserId}", commentId, userId);

        return Result.Success();
    }

    private async Task<Result<CommentDto>> GetCommentByIdInternalAsync(Guid commentId)
    {
        var comment = await _context.TaskComments
            .Include(tc => tc.User)
            .FirstOrDefaultAsync(tc => tc.Id == commentId);

        if (comment == null)
            return Error.NotFound("Comment not found");

        return new CommentDto
        {
            Id = comment.Id,
            TaskId = comment.TaskId,
            Content = comment.Content,
            UserId = comment.UserId,
            UserName = comment.User.UserName,
            CreatedAt = comment.CreatedAt,
            UpdatedAt = comment.UpdatedAt
        };
    }
}