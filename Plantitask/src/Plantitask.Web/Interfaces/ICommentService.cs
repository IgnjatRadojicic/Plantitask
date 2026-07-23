using Plantitask.Web.Models;

using Plantitask.Core.Common;
using Plantitask.Core.DTO.Comments;
namespace Plantitask.Web.Interfaces;

public interface ICommentService
{
    Task<ServiceResult<CommentDto>> AddCommentAsync(Guid taskId, CreateCommentDto model);
    Task<ServiceResult<PaginatedList<CommentDto>>> GetCommentsAsync(Guid taskId, int page = 1, int pageSize = 20);
    Task<ServiceResult<CommentDto>> UpdateCommentAsync(Guid taskId, Guid commentId, UpdateCommentDto model);
    Task<ServiceResult<bool>> DeleteCommentAsync(Guid taskId, Guid commentId);
}