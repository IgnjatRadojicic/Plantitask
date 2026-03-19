using Plantitask.Web.Models;

namespace Plantitask.Web.Interfaces
{
    public interface IGroupService
    {
        Task<ServiceResult<List<GroupDto>>> GetGUserGroupsAsync();
        Task<ServiceResult<GroupDetailsDto>> GetGroupDetailsAsync(Guid groupId);
        Task<ServiceResult<GroupDto>> CreateGroupAsync(CreateGroupRequest request);
        Task<ServiceResult<GroupDto>> JoinGroupAsync(JoinGroupRequest request);

        Task<ServiceResult<GroupMemberDto>> ChangeUserRoleAsync(Guid groupId, Guid memberId, int newRoleId);
        Task<ServiceResult<object>> RemoveMemberAsync(Guid groupId, Guid memberId);
        Task<ServiceResult<MessageResponse>> LeaveGroupAsync(Guid groupId);
    }
}
