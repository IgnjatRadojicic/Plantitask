using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

using Plantitask.Core.Common;
using Plantitask.Core.DTO.Comments;
namespace Plantitask.Web.Services;

public class CommentService : BaseApiService, ICommentService
{
    public CommentService(HttpClient http) : base(http) { }

    public Task<ServiceResult<CommentDto>> AddCommentAsync(Guid taskId, CreateCommentDto model)
        => PostAsync<CommentDto>($"api/tasks/{taskId}/comments", model);

    public Task<ServiceResult<PaginatedList<CommentDto>>> GetCommentsAsync(Guid taskId, int page = 1, int pageSize = 20)
        => GetAsync<PaginatedList<CommentDto>>($"api/tasks/{taskId}/comments?pageNumber={page}&pageSize={pageSize}");

    public Task<ServiceResult<CommentDto>> UpdateCommentAsync(Guid taskId, Guid commentId, UpdateCommentDto model)
        => PutAsync<CommentDto>($"api/tasks/{taskId}/comments/{commentId}", model);

    public Task<ServiceResult<bool>> DeleteCommentAsync(Guid taskId, Guid commentId)
        => DeleteAsync<bool>($"api/tasks/{taskId}/comments/{commentId}");
}