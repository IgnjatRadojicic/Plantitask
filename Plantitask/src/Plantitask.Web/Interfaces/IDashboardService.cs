using Plantitask.Web.Models;

using Plantitask.Contracts.DTO.Dashboard;
namespace Plantitask.Web.Interfaces
{
    public interface IDashboardService
    {

        Task<ServiceResult<GroupStatisticsDto>> GetGroupStatisticsAsync(Guid groupId);
        Task<ServiceResult<List<FieldTreeDto>>> GetFieldDataAsync();
        Task<ServiceResult<PersonalDashboardDto>> GetPersonalDashboardAsync();
    }
}
