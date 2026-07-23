using System.ComponentModel.DataAnnotations;

namespace Plantitask.Core.DTO.Kanban;

public class MoveTaskDto
{
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "StatusId must be greater than 0")]
    public int NewStatusId { get; set; }

    [Required]
    [Range(0, int.MaxValue, ErrorMessage = "DisplayOrder cannot be negative")]
    public int NewDisplayOrder { get; set; }
}