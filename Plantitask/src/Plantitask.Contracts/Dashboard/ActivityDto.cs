
namespace Plantitask.Core.DTO.Dashboard
{
    public class ActivityDto
    {
        public string UserName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string? EntityName { get; set; }
        public string? GroupName { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
