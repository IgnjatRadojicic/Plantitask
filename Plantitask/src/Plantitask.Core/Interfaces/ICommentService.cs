using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Comments;

namespace Plantitask.Core.Interfaces
{
    public interface ICommentService
    {
        Task<Result<CommentDto>> AddCommentAsync(Guid taskId, CreateCommentDto createCommentDto, Guid userId);
        Task<Result<PaginatedList<CommentDto>>> GetTaskCommentsAsync(Guid taskId, Guid userId, int pageNumber = 1, int pageSize = 20);
        Task<Result<CommentDto>> UpdateCommentAsync(Guid commentId, UpdateCommentDto updateCommentDto, Guid userId);
        Task<Result> DeleteCommentAsync(Guid commentId, Guid userId);
    }
}