using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

using Plantitask.Core.DTO.Dashboard;
namespace Plantitask.Web.Services
{
    public class DashboardService : BaseApiService, IDashboardService
    {
        public DashboardService(HttpClient http) : base(http) {}

        public Task<ServiceResult<GroupStatisticsDto>> GetGroupStatisticsAsync(Guid groupId)
    => GetAsync<GroupStatisticsDto>($"api/dashboard/groups/{groupId}");

        public async Task<ServiceResult<PersonalDashboardDto>> GetPersonalDashboardAsync()
        {
            return await GetAsync<PersonalDashboardDto>("api/dashboard/personal");
        }

        public async Task<ServiceResult<List<FieldTreeDto>>> GetFieldDataAsync()
        {
            return await GetAsync<List<FieldTreeDto>>("api/dashboard/field");
        }
    }
}
