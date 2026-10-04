using Plantitask.Web.Helpers;
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
            var timeZoneId = Uri.EscapeDataString(BrowserTimeZone.Id);
            return await GetAsync<PersonalDashboardDto>($"api/dashboard/personal?timeZoneId={timeZoneId}");
        }

        public async Task<ServiceResult<List<FieldTreeDto>>> GetFieldDataAsync()
        {
            return await GetAsync<List<FieldTreeDto>>("api/dashboard/field");
        }
    }
}
