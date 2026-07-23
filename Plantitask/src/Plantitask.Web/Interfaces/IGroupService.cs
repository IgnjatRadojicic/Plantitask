using Plantitask.Core.DTO.Groups;
using Plantitask.Core.Enums;
using Plantitask.Web.Models;

namespace Plantitask.Web.Interfaces
{
    public interface IGroupService
    {
        Task<ServiceResult<List<GroupDto>>> GetGUserGroupsAsync();
        Task<ServiceResult<GroupDetailsDto>> GetGroupDetailsAsync(Guid groupId);
        Task<ServiceResult<GroupDto>> CreateGroupAsync(CreateGroupDto request);
        Task<ServiceResult<GroupDto>> JoinGroupAsync(JoinGroupDto request);

        Task<ServiceResult<GroupMemberDto>> ChangeUserRoleAsync(Guid groupId, Guid memberId, GroupRole newRole);
        Task<ServiceResult<object>> RemoveMemberAsync(Guid groupId, Guid memberId);
        Task<ServiceResult<MessageResponse>> LeaveGroupAsync(Guid groupId);
    }
}
