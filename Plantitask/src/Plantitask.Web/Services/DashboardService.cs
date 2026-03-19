using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

namespace Plantitask.Web.Services
{
    public class DashboardService : BaseApiService, IDashboardService
    {
        public DashboardService(HttpClient http) : base(http) {}

        public Task<ServiceResult<GroupStatisticsModel>> GetGroupStatisticsAsync(Guid groupId)
    => GetAsync<GroupStatisticsModel>($"api/dashboard/groups/{groupId}/statistics");

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
