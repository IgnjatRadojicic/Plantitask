using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Plantitask.Core.Common;
using Plantitask.Core.Enums;

namespace Plantitask.Core.Entities
{
    public class NotificationPreference : BaseEntity
    {
        public Guid UserId { get; set; }
        public NotificationType Type { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int? ReminderHoursBefore { get; set; }

        public bool IsEmailEnabled { get; set; } = true;
        public User User { get; set; } = null!;
    }
}
