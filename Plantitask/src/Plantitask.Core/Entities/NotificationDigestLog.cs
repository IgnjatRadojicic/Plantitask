using System;
using Plantitask.Core.Common;
using Plantitask.Core.Enums;

namespace Plantitask.Core.Entities
{
    public class NotificationDigestLog : ImmutableEntity
    {
        public Guid UserId { get; set; }
        public NotificationType Type { get; set; }
        public DateOnly SentOn { get; set; }
    }
}
