using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManagement.Core.Common;
using TaskManagement.Core.DTO.Comments;

namespace TaskManagement.Core.Interfaces
{
    public interface ICommentService
    {
        Task<Result<CommentDto>> AddCommentAsync(Guid taskId, CreateCommentDto createCommentDto, Guid userId);
        Task<Result<List<CommentDto>>> GetTaskCommentsAsync(Guid taskId, Guid userId);
        Task<Result<CommentDto>> UpdateCommentAsync(Guid commentId, UpdateCommentDto updateCommentDto, Guid userId);
        Task<Result> DeleteCommentAsync(Guid commentId, Guid userId);
    }
}