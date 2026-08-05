

using Plantitask.Core.DTO.Comments;
using Plantitask.Core.Entities;
using System.Linq.Expressions;

namespace Plantitask.Core.Projections
{
    public static class CommentProjections
    {
        public static Expression<Func<TaskComment, CommentDto>> ToDto => tc => new CommentDto
        {
            Id = tc.Id,
            TaskId = tc.TaskId,
            Content = tc.Content,
            UserId = tc.CreatedBy,
            ProfilePicturePath = tc.Author.ProfilePicturePath,
            UserName = tc.Author.UserName,
            CreatedAt = tc.CreatedAt,
            UpdatedAt = tc.UpdatedAt
        };
    }
}
