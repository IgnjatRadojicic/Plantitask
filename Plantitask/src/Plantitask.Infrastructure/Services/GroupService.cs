using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
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

        public async Task<Result<GroupDto>> CreateGroupAsync(CreateGroupDto createGroupDto, Guid userId)
        {
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
                CreatedAt = DateTime.UtcNow
            };

            _context.Groups.Add(group);

            var ownerMember = new GroupMember
            {
                GroupId = group.Id,
                UserId = userId,
                RoleId = (int)GroupRole.Owner,
                JoinedAt = DateTime.UtcNow,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.GroupMembers.Add(ownerMember);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Group created: {GroupName} with code {GroupCode} by user {UserId}",
                group.Name, group.GroupCode, userId);

            return await MapToGroupDtoAsync(group, userId);
        }

        public async Task<Result<GroupDto>> JoinGroupAsync(JoinGroupDto joinGroupDto, Guid userId)
        {
            var group = await _context.Groups
                .FirstOrDefaultAsync(g => g.GroupCode == joinGroupDto.GroupCode);

            if (group == null)
                return Error.NotFound("Invalid group code");

            if (!group.IsActive)
                return Error.BadRequest("This group is no longer active");

            var existingMember = await _context.GroupMembers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(gm => gm.GroupId == group.Id && gm.UserId == userId);

            if (existingMember != null)
            {
                if (existingMember.IsDeleted)
                {
                    existingMember.IsDeleted = false;
                    existingMember.DeletedAt = null;
                    existingMember.DeletedBy = null;
                    existingMember.JoinedAt = DateTime.UtcNow;
                    existingMember.UpdatedBy = userId;
                    existingMember.UpdatedAt = DateTime.UtcNow;
                    existingMember.RoleId = (int)GroupRole.Member;

                    await _context.SaveChangesAsync();

                    _logger.LogInformation("User {UserId} rejoined group {GroupId}", userId, group.Id);

                    return await MapToGroupDtoAsync(group, userId);
                }
                else
                {
                    return Error.Conflict("You are already a member of this group");
                }
            }

            if (!string.IsNullOrEmpty(group.PasswordHash))
            {
                if (string.IsNullOrEmpty(joinGroupDto.Password))
                    return Error.Forbidden("This group requires a password");

                if (!_passwordHasher.VerifyPassword(joinGroupDto.Password, group.PasswordHash))
                    return Error.Forbidden("Incorrect group password");
            }

            var newMember = new GroupMember
            {
                GroupId = group.Id,
                UserId = userId,
                RoleId = (int)GroupRole.Member,
                JoinedAt = DateTime.UtcNow,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.GroupMembers.Add(newMember);
            await _context.SaveChangesAsync();

            _logger.LogInformation("User {UserId} joined group {GroupId}", userId, group.Id);

            return await MapToGroupDtoAsync(group, userId);
        }

        public async Task<Result<List<GroupDto>>> GetUserGroupsAsync(Guid userId)
        {
            var groups = await _context.GroupMembers
                .Where(gm => gm.UserId == userId)
                .Include(gm => gm.Group)
                .Include(gm => gm.Role)
                .Select(gm => new GroupDto
                {
                    Id = gm.Group.Id,
                    Name = gm.Group.Name,
                    GroupCode = gm.Group.GroupCode,
                    IsPasswordProtected = !string.IsNullOrEmpty(gm.Group.PasswordHash),
                    MemberCount = _context.GroupMembers.Count(m => m.GroupId == gm.GroupId),
                    UserRole = (GroupRole)gm.RoleId,
                    JoinedAt = gm.JoinedAt,
                    CreatedAt = gm.Group.CreatedAt
                }).ToListAsync();

            return groups;
        }

        public async Task<Result<GroupDetailsDto>> GetGroupDetailsAsync(Guid groupId, Guid userId)
        {
            var membership = await _context.GroupMembers
                .Include(gm => gm.Role)
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (membership == null)
                return Error.Forbidden("You are not a member of this group");

            var group = await _context.Groups
                .Include(g => g.Owner)
                .FirstOrDefaultAsync(g => g.Id == groupId);

            if (group == null)
                return Error.NotFound("Group not found");

            var members = await _context.GroupMembers
                .Where(gm => gm.GroupId == groupId)
                .Include(gm => gm.User)
                .Include(gm => gm.Role)
                .Select(gm => new GroupMemberDto
                {
                    UserId = gm.UserId,
                    UserName = gm.User.UserName,
                    Email = gm.User.Email,
                    Role = (GroupRole)gm.RoleId,
                    JoinedAt = gm.JoinedAt
                }).ToListAsync();

            return new GroupDetailsDto
            {
                Id = group.Id,
                Name = group.Name,
                GroupCode = group.GroupCode,
                IsPasswordProtected = !string.IsNullOrEmpty(group.PasswordHash),
                OwnerId = group.OwnerId,
                OwnerName = group.Owner.UserName,
                CreatedAt = group.CreatedAt,
                Members = members
            };
        }

        public async Task<Result<GroupDto>> UpdateGroupAsync(Guid groupId, UpdateGroupDto updateGroupDto, Guid userId)
        {
            var group = await _context.Groups.FindAsync(groupId);

            if (group == null)
                return Error.NotFound("Group not found");

            var membership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (membership == null)
                return Error.Forbidden("You are not a member of this group");

            var userRole = (GroupRole)membership.RoleId;

            if (userRole != GroupRole.Owner && userRole != GroupRole.Manager)
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
            group.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Group {GroupId} updated by user {UserId}", groupId, userId);

            return await MapToGroupDtoAsync(group, userId);
        }

        public async Task<Result<GroupMemberDto>> ChangeUserRoleAsync(
            Guid groupId, Guid memberId, ChangeRoleDto changeRoleDto, Guid userId)
        {
            _logger.LogInformation("User {UserId} changing role for member {MemberId} in group {GroupId}",
                userId, memberId, groupId);

            var requestingMembership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (requestingMembership == null)
                return Error.Forbidden("You are not a member of this group");

            var requestingRole = (GroupRole)requestingMembership.RoleId;
            if (requestingRole != GroupRole.Owner && requestingRole != GroupRole.Manager)
                return Error.Forbidden("Only Owner or Manager can change roles");

            var targetMembership = await _context.GroupMembers
                .Include(gm => gm.User)
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == memberId);

            if (targetMembership == null)
                return Error.NotFound("Member not found in this group");

            if ((GroupRole)targetMembership.RoleId == GroupRole.Owner)
                return Error.BadRequest("Cannot change the role of the group owner");

            targetMembership.RoleId = (int)changeRoleDto.NewRole;
            targetMembership.UpdatedBy = userId;
            targetMembership.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Role changed for member {MemberId} to {NewRole}", memberId, changeRoleDto.NewRole);

            return new GroupMemberDto
            {
                UserId = targetMembership.UserId,
                UserName = targetMembership.User.UserName,
                Email = targetMembership.User.Email,
                Role = changeRoleDto.NewRole,
                JoinedAt = targetMembership.JoinedAt
            };
        }

        public async Task<Result> RemoveUserFromGroupAsync(Guid groupId, Guid memberId, Guid userId)
        {
            _logger.LogInformation("User {UserId} removing member {MemberId} from group {GroupId}",
                userId, memberId, groupId);

            var requestingMembership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (requestingMembership == null)
                return Error.Forbidden("You are not a member of this group");

            var requestingRole = (GroupRole)requestingMembership.RoleId;
            if (requestingRole != GroupRole.Owner && requestingRole != GroupRole.Manager)
                return Error.Forbidden("Only Owner or Manager can remove members");

            var targetMembership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == memberId);

            if (targetMembership == null)
                return Error.NotFound("Member not found in this group");

            if ((GroupRole)targetMembership.RoleId == GroupRole.Owner)
                return Error.BadRequest("Cannot remove the group owner");

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

        private async Task<GroupDto> MapToGroupDtoAsync(Group group, Guid userId)
        {
            var memberCount = await _context.GroupMembers
                .CountAsync(gm => gm.GroupId == group.Id);

            var userMembership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == group.Id && gm.UserId == userId);

            return new GroupDto
            {
                Id = group.Id,
                Name = group.Name,
                GroupCode = group.GroupCode,
                IsPasswordProtected = !string.IsNullOrEmpty(group.PasswordHash),
                MemberCount = memberCount,
                UserRole = userMembership != null ? (GroupRole)userMembership.RoleId : GroupRole.Member,
                JoinedAt = userMembership?.JoinedAt ?? DateTime.UtcNow,
                CreatedAt = group.CreatedAt
            };
        }

        public async Task<string> GenerateUniqueGroupCode(string groupName)
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
    }
}