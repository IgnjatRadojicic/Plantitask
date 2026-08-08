using Plantitask.Core.DTO.Notifications;
using Plantitask.Core.Entities;
using System.Linq.Expressions;

namespace Plantitask.Core.Projections
{
    public static class NotificationProjections
    {
        public static Expression<Func<Notification, NotificationDto>> ToDto => n => new NotificationDto
        {
            Id = n.Id,
            UserId = n.UserId,
            ActorId = n.ActorId,
            ActorName = n.Actor != null ? n.Actor.UserName : null,
            Type = n.Type,
            TypeName = n.Type.ToString(),
            Title = n.Title,
            Message = n.Message,
            RelatedEntityId = n.RelatedEntityId,
            RelatedEntityType = n.RelatedEntityType,
            RelatedDate = n.RelatedDate,
            IsRead = n.IsRead,
            ReadAt = n.ReadAt,
            CreatedAt = n.CreatedAt
        };
    }
}
