using Plantitask.Web.Models;

namespace Plantitask.Web.Interfaces
{
    public interface IDashboardService
    {

        Task<ServiceResult<GroupStatisticsModel>> GetGroupStatisticsAsync(Guid groupId);
        Task<ServiceResult<List<FieldTreeDto>>> GetFieldDataAsync();
        Task<ServiceResult<PersonalDashboardDto>> GetPersonalDashboardAsync();
    }
}
