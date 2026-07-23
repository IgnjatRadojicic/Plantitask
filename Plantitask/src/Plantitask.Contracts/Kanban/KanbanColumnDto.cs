namespace Plantitask.Core.DTO.Kanban;

public class KanbanColumnDto
{
    public int StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public List<KanbanTaskDto> Tasks { get; set; } = new();
}