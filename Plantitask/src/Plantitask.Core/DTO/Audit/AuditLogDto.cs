using Plantitask.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Plantitask.Core.DTO.Audit
{
    public class AuditLogDto
    {
        public Guid Id { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? PropertyName { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }

        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; }
        public string? Reason { get; set; }
        public string IpAddress { get; set; } = string.Empty;

        public static Expression<Func<AuditLog, AuditLogDto>> Projection => a => new AuditLogDto
        {
            Id = a.Id,
            EntityType = a.EntityType,
            EntityId = a.EntityId,
            Action = a.Action,
            PropertyName = a.PropertyName,
            OldValue = a.OldValue,
            NewValue = a.NewValue,
            UserId = a.UserId,
            UserName = a.UserName,
            UserEmail = a.UserEmail,
            Timestamp = a.CreatedAt,
            Reason = a.Reason,
            IpAddress = a.IpAddress
        };
    }
}
