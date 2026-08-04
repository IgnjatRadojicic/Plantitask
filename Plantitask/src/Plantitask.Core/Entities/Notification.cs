using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Common;
using Plantitask.Core.Enums;

namespace Plantitask.Core.Entities
{
    public class Notification : SelfManagedEntity
    {
        public Guid UserId { get; set; }
        public Guid? ActorId { get; set; }
        public NotificationType Type { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Guid? RelatedEntityId { get; set; }
        public string? RelatedEntityType { get; set; }
        public DateTime? RelatedDate { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }

        // Navigation properties
        public User User { get; set; } = null!;
        public User? Actor { get; set; }
    }
}
