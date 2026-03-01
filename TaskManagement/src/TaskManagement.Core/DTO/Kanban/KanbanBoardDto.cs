namespace TaskManagement.Core.DTO.Kanban;

public class KanbanBoardDto
{
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public List<KanbanColumnDto> Columns { get; set; } = new();
}