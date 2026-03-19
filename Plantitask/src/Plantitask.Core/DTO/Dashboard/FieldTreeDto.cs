

using Plantitask.Core.Enums;

namespace Plantitask.Core.DTO.Dashboard
{
    public class FieldTreeDto
    {
       public Guid GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public double CompletionPercentage { get; set; }
        public TreeStage CurrentTreeStage { get; set; }
        public int MemberCount { get; set; }
        public int TotalTasks { get; set; }
        public int CompletedTasks { get; set; }

    }
}
