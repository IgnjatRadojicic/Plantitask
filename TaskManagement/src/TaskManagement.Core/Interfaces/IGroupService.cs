using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskManagement.Core.Common;
using TaskManagement.Core.DTO.Groups;

namespace TaskManagement.Core.Interfaces
{
    public interface IGroupService
    {
        Task<Result<GroupDto>> CreateGroupAsync(CreateGroupDto createGroupDto, Guid userId);
        Task<Result<GroupDto>> JoinGroupAsync(JoinGroupDto joinGroupDto, Guid userId);
        Task<Result<List<GroupDto>>> GetUserGroupsAsync(Guid userId);
        Task<Result<GroupDetailsDto>> GetGroupDetailsAsync(Guid groupId, Guid userId);
        Task<Result<GroupDto>> UpdateGroupAsync(Guid groupId, UpdateGroupDto updateGroupDto, Guid userId);
        Task<Result<GroupMemberDto>> ChangeUserRoleAsync(Guid groupId, Guid memberId, ChangeRoleDto changeRoleDto, Guid userId);
        Task<Result> RemoveUserFromGroupAsync(Guid groupId, Guid memberId, Guid userId);
        Task<Result> LeaveGroupAsync(Guid groupId, Guid userId);
        Task<string> GenerateUniqueGroupCode(string groupName);
    }
}
