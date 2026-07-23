using Plantitask.Web.Models;

using Plantitask.Core.DTO.Notifications;
namespace Plantitask.Web.Interfaces;

public interface INotificationPreferenceService
{
    Task<ServiceResult<List<NotificationPreferenceDto>>> GetPreferencesAsync();
    Task<ServiceResult<MessageResponse>> SavePreferencesAsync(UpdateNotificationPreferencesDto dto);
}