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
        public Task<ServiceResult<GroupMemberDto>> ChangeUserRoleAsync(Guid groupId, Guid memberId, int newRoleId)
         => PutAsync<GroupMemberDto>($"api/groups/{groupId}/members/{memberId}/role", new { NewRole = newRoleId });

        public Task<ServiceResult<object>> RemoveMemberAsync(Guid groupId, Guid memberId)
            => DeleteAsync<object>($"api/groups/{groupId}/members/{memberId}");

        public Task<ServiceResult<UserProfileDto>> UpdateProfileAsync(UpdateUserProfileDto dto)
         => PutAsync<UserProfileDto>("api/user/profile", dto);

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
