using Plantitask.Web.Models;

namespace Plantitask.Web.Interfaces;

public interface ICommentService
{
    Task<ServiceResult<CommentDto>> AddCommentAsync(Guid taskId, CreateCommentDto model);
    Task<ServiceResult<PaginatedResult<CommentDto>>> GetCommentsAsync(Guid taskId, int page = 1, int pageSize = 20);
    Task<ServiceResult<CommentDto>> UpdateCommentAsync(Guid taskId, Guid commentId, UpdateCommentDto model);
    Task<ServiceResult<bool>> DeleteCommentAsync(Guid taskId, Guid commentId);
}