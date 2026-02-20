using System.ComponentModel.DataAnnotations;
using TaskManagement.Core.Enums;

namespace TaskManagement.Core.DTO.Notifications;

public class NotificationPreferenceDto
{
    public NotificationType Type { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public int? ReminderHoursBefore { get; set; }
}

public class UpdateNotificationPreferencesDto
{
    public List<NotificationPreferenceUpdateItem> Preferences { get; set; } = new();
}

public class NotificationPreferenceUpdateItem
{

    [Required]
    public NotificationType Type { get; set; }

    [Required]
    public bool IsEnabled { get; set; }

    [Range(1, 168)]
    public int? ReminderHoursBefore { get; set; }
}