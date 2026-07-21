using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.Constants;
using Plantitask.Core.DTO.Groups;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    public class GroupService : IGroupService
    {
        private readonly IApplicationDbContext _context;
        private readonly IGroupCodeGenerator _codeGenerator;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ILogger<GroupService> _logger;

        public GroupService(
            IApplicationDbContext context,
            IGroupCodeGenerator codeGenerator,
            IPasswordHasher passwordHasher,
            ILogger<GroupService> logger)
        {
            _context = context;
            _codeGenerator = codeGenerator;
            _passwordHasher = passwordHasher;
            _logger = logger;
        }

        public async Task<bool> IsUserMemberAsync(Guid groupId, Guid userId)
        {
            return await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == userId);
        }

        public async Task<int?> GetUserPermissionLevelAsync(Guid groupId, Guid userId)
        {
            return await _context.GroupMembers
                .Where(gm => gm.GroupId == groupId && gm.UserId == userId)
                .Select(gm => (int?)gm.Role.PermissionLevel)
                .FirstOrDefaultAsync();
        }

        public async Task<Result<GroupDto>> CreateGroupAsync(CreateGroupDto createGroupDto, Guid userId)
        {

            var userData = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => new { u.MaxGroups })
                .FirstOrDefaultAsync();

            if (userData == null)
                return Error.NotFound("User not found");

            var currentGroupCount = await _context.GroupMembers
                .CountAsync(gm => gm.UserId == userId);

            if (currentGroupCount >= userData.MaxGroups)
                return Error.Forbidden($"You've reached your limit of {userData.MaxGroups} groups. Upgrade to Premium for more.");


            var groupCode = await GenerateUniqueGroupCode(createGroupDto.Name);

            string? passwordHash = null;
            if (!string.IsNullOrEmpty(createGroupDto.Password))
                passwordHash = _passwordHasher.HashPassword(createGroupDto.Password);

            var group = new Group
            {
                Name = createGroupDto.Name,
                GroupCode = groupCode,
                PasswordHash = passwordHash,
                IsActive = true,
                OwnerId = userId,
                CreatedBy = userId,
            };

            _context.Groups.Add(group);

            var ownerMember = new GroupMember
            {
                GroupId = group.Id,
                UserId = userId,
                RoleId = (int)GroupRole.Owner,
                CreatedBy = userId,
            };

            _context.GroupMembers.Add(ownerMember);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Group created: {GroupName} with code {GroupCode} by user {UserId}",
                group.Name, group.GroupCode, userId);


            return new GroupDto
            {
                Id = group.Id,
                Name = group.Name,
                GroupCode = group.GroupCode,
                IsPasswordProtected = !string.IsNullOrEmpty(group.PasswordHash),
                MemberCount = 1,
                UserRole = GroupRole.Owner,
            };
        }

        public async Task<Result<GroupDto>> JoinGroupAsync(JoinGroupDto joinGroupDto, Guid userId)
        {
            var groupData = await _context.Groups
                .Where(g => g.GroupCode == joinGroupDto.GroupCode)
                .Select(g => new
                {
                    g.Id,
                    g.Name,
                    g.GroupCode,
                    g.PasswordHash,
                    g.IsActive,
                })
                .FirstOrDefaultAsync();

            if (groupData == null)
                return Error.NotFound("Invalid group code");

            if (!groupData.IsActive)
                return Error.BadRequest("This group is no longer active");


            var existingMember = await _context.GroupMembers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(gm => gm.GroupId == groupData.Id && gm.UserId == userId);

            if (existingMember != null && !existingMember.IsDeleted)
            {
                return Error.Conflict("You already a member of this group");
            }

            if (!string.IsNullOrEmpty(groupData.PasswordHash))
            {
                if (string.IsNullOrEmpty(joinGroupDto.Password))
                    return Error.Forbidden("This group requires a password");
                if (!_passwordHasher.VerifyPassword(joinGroupDto.Password, groupData.PasswordHash))
                {
                    return Error.Forbidden("Incorrect group password");
                }
            }

            var userData = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => new { u.MaxGroups })
                .FirstOrDefaultAsync();

            if (userData == null)
                return Error.NotFound("User not found");

            var currentGroupCount = await _context.GroupMembers
                .CountAsync(gm => gm.UserId == userId);

            if (currentGroupCount >= userData?.MaxGroups)
                return Error.Forbidden($"You've reached your limit of {userData.MaxGroups} Trees. Upgrade to Premium for more.");

            var Rejoin = existingMember != null;

            if (existingMember != null)

            {
                existingMember.IsDeleted = false;
                existingMember.DeletedAt = null;
                existingMember.DeletedBy = null;
                existingMember.UpdatedBy = userId;
                existingMember.RoleId = (int)GroupRole.Member;

            } else
            {
                _context.GroupMembers.Add(new GroupMember
                {
                    GroupId = groupData.Id,
                    UserId = userId,
                    RoleId = (int)GroupRole.Member,
                    CreatedBy = userId
                });
            }

            await _context.SaveChangesAsync();

            var memberCount = await _context.GroupMembers
                .CountAsync(gm => gm.GroupId == groupData.Id);

            _logger.LogInformation("User {UserId} {Action} group {GroupId}",
                userId, Rejoin ? "rejoined" : "joined", groupData.Id);

            return new GroupDto
            {
                Id = groupData.Id,
                Name = groupData.Name,
                GroupCode = groupData.GroupCode,
                IsPasswordProtected = !string.IsNullOrEmpty(groupData.PasswordHash),
                MemberCount = memberCount,
                UserRole = GroupRole.Member,
            };

        }

        public async Task<Result<List<GroupDto>>> GetUserGroupsAsync(Guid userId)
        {
            var groups = await _context.GroupMembers
                .Where(gm => gm.UserId == userId)
                .Select(gm => new GroupDto
                {
                    Id = gm.Group.Id,
                    Name = gm.Group.Name,
                    GroupCode = gm.Group.GroupCode,
                    IsPasswordProtected = !string.IsNullOrEmpty(gm.Group.PasswordHash),
                    MemberCount = gm.Group.Members.Count
                }).ToListAsync();

            return groups;
        }

        public async Task<Result<GroupDetailsDto>> GetGroupDetailsAsync(Guid groupId, Guid userId)
        {

            var group = await _context.Groups
                .Where(g => g.Id == groupId)
                .Select(g => new
                {
                    g.Id,
                    g.Name,
                    g.GroupCode,
                    g.PasswordHash,
                    g.OwnerId,
                    OwnerName = g.Owner.UserName,
                })
                .FirstOrDefaultAsync();

            if (group == null)
                return Error.NotFound("Group not found");

            var isMember = await IsUserMemberAsync(groupId, userId);

            if (!isMember)
                return Error.Forbidden("You are not a member of this group");


            var members = await _context.GroupMembers
                .Where(gm => gm.GroupId == groupId)
                .Select(gm => new GroupMemberDto
                {
                    UserId = gm.UserId,
                    UserName = gm.User.UserName,
                    Email = gm.User.Email,
                    ProfilePictureUrl = gm.User.ProfilePictureUrl,
                    Role = (GroupRole)gm.RoleId,
                }).ToListAsync();

            return new GroupDetailsDto
            {
                Id = group.Id,
                Name = group.Name,
                GroupCode = group.GroupCode,
                IsPasswordProtected = !string.IsNullOrEmpty(group.PasswordHash),
                OwnerId = group.OwnerId,
                OwnerName = group.OwnerName,
                Members = members
            };
        }

        public async Task<Result<GroupDto>> UpdateGroupAsync(Guid groupId, UpdateGroupDto updateGroupDto, Guid userId)
        {
            var group = await _context.Groups.FindAsync(groupId);

            if (group == null)
                return Error.NotFound("Group not found");

            var permissionLevel = await GetUserPermissionLevelAsync(groupId, userId);

            if (permissionLevel == null)
                return Error.Forbidden("You are not a member of this group");

            if (permissionLevel < PermissionLevels.Manager)
                return Error.Forbidden("Only Owner or Manager can update group details");

            if (!string.IsNullOrEmpty(updateGroupDto.Name))
                group.Name = updateGroupDto.Name;

            if (updateGroupDto.Password != null)
            {
                group.PasswordHash = string.IsNullOrEmpty(updateGroupDto.Password)
                    ? null
                    : _passwordHasher.HashPassword(updateGroupDto.Password);
            }

            group.UpdatedBy = userId;
            await _context.SaveChangesAsync();

            var memberCount = await _context.GroupMembers
                .CountAsync(gm => gm.GroupId == groupId);

            _logger.LogInformation("Group {GroupId} updated by user {UserId}", groupId, userId);

            return new GroupDto
            {
                Id = group.Id,
                Name = group.Name,
                GroupCode = group.GroupCode,
                IsPasswordProtected = !string.IsNullOrEmpty(group.PasswordHash),
                MemberCount = memberCount,
                UserRole = permissionLevel >= PermissionLevels.Owner ? GroupRole.Owner : GroupRole.Manager,
            };
        }

        public async Task<Result<GroupMemberDto>> ChangeUserRoleAsync(
            Guid groupId, Guid memberId, ChangeRoleDto changeRoleDto, Guid userId)
        {
            _logger.LogInformation("User {UserId} changing role for member {MemberId} in group {GroupId}",
                userId, memberId, groupId);

            var permissionLevel =  await GetUserPermissionLevelAsync(groupId, userId);

            if (permissionLevel == null)
                return Error.Forbidden("You are not a member of this group");

            if (permissionLevel < PermissionLevels.Manager)
                return Error.Forbidden("Only Owner or Manager can change roles");

            if (memberId == userId)
                return Error.BadRequest("You cannot change your own role");

            if (changeRoleDto.NewRole == GroupRole.Owner)
                return Error.BadRequest("Ownership cannot be assigned through a role change");

            var newRoleLevel = RoleLevel(changeRoleDto.NewRole);
            if (newRoleLevel == null)
                return Error.BadRequest("Invalid role");

            var targetMembership = await _context.GroupMembers
                .Include(gm => gm.User)
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == memberId);

            if (targetMembership == null)
                return Error.NotFound("Member not found in this group");

            var targetLevel = RoleLevel((GroupRole)targetMembership.RoleId);
            if (targetLevel == null || targetLevel >= permissionLevel)
                return Error.Forbidden("You can only change roles of members below your own role");

            if (newRoleLevel >= permissionLevel)
                return Error.Forbidden("You cannot assign a role at or above your own");


            targetMembership.RoleId = (int)changeRoleDto.NewRole;
            targetMembership.UpdatedBy = userId;
     
            await _context.SaveChangesAsync();

            _logger.LogInformation("Role changed for member {MemberId} to {NewRole}", memberId, changeRoleDto.NewRole);

            return new GroupMemberDto
            {
                UserId = targetMembership.UserId,
                UserName = targetMembership.User.UserName,
                Email = targetMembership.User.Email,
                ProfilePictureUrl = targetMembership.User.ProfilePictureUrl,
                Role = changeRoleDto.NewRole
            };
        }

        public async Task<Result> TransferOwnershipAsync(Guid groupId, Guid newOwnerId, Guid userId)
        {
            if (newOwnerId == userId)
                return Error.BadRequest("Can't transfer ownership to yourself");

            var permissionLevel = await GetUserPermissionLevelAsync(groupId, userId);

            if (permissionLevel == null)
                return Error.Forbidden("You are not a member of this group");

            if (permissionLevel < PermissionLevels.Owner)
                return Error.Forbidden("Only the owner can transfer ownership");

            var newOwnerMembership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == newOwnerId);
            if (newOwnerMembership == null)
                return Error.NotFound("New oner must be a group member");

            var currentOwnerMembership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);
            var group = await _context.Groups.FindAsync(groupId);

            group!.OwnerId = newOwnerId;
            newOwnerMembership.RoleId = (int)GroupRole.Owner;
            currentOwnerMembership!.RoleId = (int)GroupRole.Manager;

            await _context.SaveChangesAsync();
            return Result.Success();
        }

        public async Task<Result> DeleteGroupAsync(Guid groupId, Guid userId)
        {
            var permissionLevel = await GetUserPermissionLevelAsync(groupId, userId);

            if (permissionLevel == null)
                return Error.Forbidden("You are not a member of this group");

            if (permissionLevel < PermissionLevels.Owner)
                return Error.Forbidden("Only the owner can delete the group");

            var memberCount = await _context.GroupMembers
                .CountAsync(gm => gm.GroupId == groupId);

            if (memberCount > 1)
                return Error.BadRequest("Remove all other members before deleting the group");

            var group = await _context.Groups.FindAsync(groupId);
            if (group == null)
                return Error.NotFound("Group not found");

            var now = DateTime.UtcNow;

            var ownerMembership = await _context.GroupMembers
                .FirstAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            await using var transaction = await _context.BeginTransactionAsync();

            await _context.TaskComments
                .IgnoreQueryFilters()
                .Where(tc => tc.Task.GroupId == groupId && !tc.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.IsDeleted, true)
                    .SetProperty(c => c.DeletedAt, now)
                    .SetProperty(c => c.DeletedBy, userId)
                    .SetProperty(c => c.UpdatedAt, now));


            await _context.TaskAttachments
                .IgnoreQueryFilters()
                .Where(ta => ta.Task.GroupId == groupId && !ta.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.IsDeleted, true)
                    .SetProperty(a => a.DeletedAt, now)
                    .SetProperty(a => a.DeletedBy, userId)
                    .SetProperty(c => c.UpdatedAt, now));

            await _context.Tasks
                .IgnoreQueryFilters()
                .Where(t => t.GroupId == groupId && !t.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.IsDeleted, true)
                    .SetProperty(t => t.DeletedAt, now)
                    .SetProperty(t => t.DeletedBy, userId)
                    .SetProperty(c => c.UpdatedAt, now));

            group.IsDeleted = true;
            group.DeletedAt = now;
            group.DeletedBy = userId;

            ownerMembership.IsDeleted = true;
            ownerMembership.DeletedAt = now;
            ownerMembership.DeletedBy = userId;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation("Group {GroupId} deleted by user {UserId}", groupId, userId);

            return Result.Success();
        }

        public async Task<Result> RemoveUserFromGroupAsync(Guid groupId, Guid memberId, Guid userId)
        {
            _logger.LogInformation("User {UserId} removing member {MemberId} from group {GroupId}",
                userId, memberId, groupId);

            if (userId == memberId)
                return Error.BadRequest("Leave the group instead");

            var permissionLevel = await GetUserPermissionLevelAsync(groupId, userId);

            if (permissionLevel == null)
                return Error.Forbidden("You are not a member of this group");

            if (permissionLevel < PermissionLevels.Manager) 
                return Error.Forbidden("Only Owner or Manager can remove members");

            var targetMembership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == memberId);

            if (targetMembership == null)
                return Error.NotFound("Member not found in this group");

            if ((GroupRole)targetMembership.RoleId == GroupRole.Owner)
                return Error.BadRequest("Cannot remove the group owner");


            var targetLevel = RoleLevel(targetMembership.RoleId);
            if (targetLevel == null || targetLevel >= permissionLevel)
                return Error.Forbidden("You can only remove members below your own role");

            targetMembership.IsDeleted = true;
            targetMembership.DeletedAt = DateTime.UtcNow;
            targetMembership.DeletedBy = userId;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Member {MemberId} removed from group {GroupId}", memberId, groupId);

            return Result.Success();
        }

        public async Task<Result> LeaveGroupAsync(Guid groupId, Guid userId)
        {
            _logger.LogInformation("User {UserId} leaving group {GroupId}", userId, groupId);

            var membership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (membership == null)
                return Error.NotFound("You are not a member of this group");

            if ((GroupRole)membership.RoleId == GroupRole.Owner)
                return Error.BadRequest("Group owner cannot leave. Transfer ownership or delete the group.");

            membership.IsDeleted = true;
            membership.DeletedAt = DateTime.UtcNow;
            membership.DeletedBy = userId;

            await _context.SaveChangesAsync();

            _logger.LogInformation("User {UserId} left group {GroupId}", userId, groupId);

            return Result.Success();
        }



        private async Task<string> GenerateUniqueGroupCode(string groupName)
        {
            string code;
            bool codeExists;

            do
            {
                code = _codeGenerator.Generate(groupName);
                codeExists = await _context.Groups.AnyAsync(g => g.GroupCode == code);
            }
            while (codeExists);

            return code;
        }

        private static int? RoleLevel(GroupRole role) => role switch
        {
            GroupRole.Owner => PermissionLevels.Owner,
            GroupRole.Manager => PermissionLevels.Manager,
            GroupRole.TeamLead => PermissionLevels.TeamLead,
            GroupRole.Member => PermissionLevels.Member,
            _ => null,
        };

        private static int? RoleLevel(int roleId) => RoleLevel((GroupRole)roleId);

    }
}