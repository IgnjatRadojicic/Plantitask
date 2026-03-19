namespace Plantitask.Web.Models;

public class NotificationDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? RelatedEntityId { get; set; }
    public string? RelatedEntityType { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UnreadCountModel
{
    public int Count { get; set; }
}
