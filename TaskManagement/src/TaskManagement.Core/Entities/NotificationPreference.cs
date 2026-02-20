using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManagement.Core.Common;
using TaskManagement.Core.Enums;

namespace TaskManagement.Core.Entities
{
    public class NotificationPreference : BaseEntity
    {
        public Guid UserId { get; set; }
        public NotificationType Type { get; set; }
        public bool IsEnabled { get; set; } = true;
        public int? ReminderHoursBefore { get; set; }

        public User User { get; set; }
    }
}
