using Plantitask.Web.Interfaces;
using Plantitask.Web.Models;

namespace Plantitask.Web.Services
{
    public class GroupService : BaseApiService, IGroupService
    {
        public GroupService(HttpClient http) : base(http)
        {
        }

        public async Task<ServiceResult<GroupDto>> CreateGroupAsync(CreateGroupRequest request)
        {
            return await PostAsync<GroupDto>("api/groups", request);
        }

        public async Task<ServiceResult<GroupDetailsDto>> GetGroupDetailsAsync(Guid groupId)
        {
            return await GetAsync<GroupDetailsDto>($"api/groups/{groupId}");
        }

        public async Task<ServiceResult<List<GroupDto>>> GetGUserGroupsAsync()
        {
            return await GetAsync<List<GroupDto>>("api/groups");
        }

        public async Task<ServiceResult<GroupDto>> JoinGroupAsync(JoinGroupRequest request)
        {
            return await PostAsync<GroupDto>("api/groups/join", request);
        }

        public async Task<ServiceResult<MessageResponse>> LeaveGroupAsync(Guid groupId)
        {
            return await PostAsync<MessageResponse>($"api/groups/{groupId}/leave", new { });
        }
    }
}
