using System;
using System.Linq.Expressions;
using Plantitask.Core.DTO.Audit;
using Plantitask.Core.Entities;

namespace Plantitask.Core.Projections
{
    public static class AuditLogProjections
    {
        public static Expression<Func<AuditLog, AuditLogDto>> ToAuditLogDto => a => new AuditLogDto
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
