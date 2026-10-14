using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Common;
using Plantitask.Core.Events;
namespace Plantitask.Core.Entities
{
    public class AuditLog : ImmutableEntity
    {
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? PropertyName { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        // Null together when nobody signed in caused it. Source then names the door it came through.
        public Guid? UserId { get; set; }
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
        public required EventSource Source { get; set; }

        // When it happened. CreatedAt is when the row was written, which is later for drained rows.
        public required DateTime OccurredAt { get; set; }

        public Guid? GroupId { get; set; }
        public string? Reason { get; set; }

        // Account rows only. Group rows never store them, see the security skill.
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }

        public User? User { get; set; }
        public Group? Group { get; set; }

    }
}
