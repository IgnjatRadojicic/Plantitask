using System.ComponentModel.DataAnnotations;
namespace Plantitask.Web.Models
{

    // ===== Comment Dtos =====

    public class UpdateCommentDto
    {
        [Required]
        [StringLength(2000, MinimumLength = 1)]
        public string Content { get; set; } = string.Empty;
    }

    public class CreateCommentDto
    {
        [Required]
        [StringLength(2000, MinimumLength = 1, ErrorMessage = "Comment cannot be empty.")]
        public string Content { get; set; } = string.Empty;
    }

    public class CommentDto
    {
        public Guid Id { get; set; }
        public Guid TaskId { get; set; }
        public string Content { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsEdited => UpdatedAt.HasValue;
    }
}
