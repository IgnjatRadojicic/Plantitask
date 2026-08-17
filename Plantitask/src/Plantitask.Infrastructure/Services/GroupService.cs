using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Groups;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// Group lifecycle and membership. The group is the tenant boundary, so this service is
    /// also the single source of truth for authorization checks - every other service asks
    /// <see cref="IsUserMemberAsync"/> or <see cref="GetUserRoleAsync"/> instead of querying
    /// the membership table itself.
    /// </summary>
    public class GroupService : IGroupService
    {
        private readonly IApplicationDbContext _context;
        private readonly IGroupCodeGenerator _codeGenerator;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IEntitlementService _entitlements;
        private readonly ILogger<GroupService> _logger;

        public GroupService(
            IApplicationDbContext context,
            IGroupCodeGenerator codeGenerator,
            IPasswordHasher passwordHasher,
            IEntitlementService entitlements,
            ILogger<GroupService> logger)
        {
            _context = context;
            _codeGenerator = codeGenerator;
            _passwordHasher = passwordHasher;
            _entitlements = entitlements;
            _logger = logger;
        }

        /// <summary>
        /// The membership check the rest of the codebase leans on. Soft-deleted memberships are
        /// excluded by the global query filter, so a removed member is simply not a member.
        /// </summary>
        public async Task<bool> IsUserMemberAsync(Guid groupId, Guid userId)
        {
            return await _context.GroupMembers
                .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == userId);
        }

        /// <summary>
        /// The caller's role in a group, or null when they are not a member. The enum's numeric
        /// values are the permission ranks, so callers compare roles directly
        /// (for example role &lt; GroupRole.Manager).
        /// </summary>
        public async Task<GroupRole?> GetUserRoleAsync(Guid groupId, Guid userId)
        {
            return await _context.GroupMembers
                .Where(gm => gm.GroupId == groupId && gm.UserId == userId)
                .Select(gm => (GroupRole?)gm.RoleId)
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Creates a group with a unique join code and makes the creator its Owner in the same
        /// save. An optional password is BCrypt-hashed; the plan's group limit is checked first.
        /// </summary>
        public async Task<Result<GroupDto>> CreateGroupAsync(CreateGroupDto createGroupDto, Guid userId)
        {

            var limitError = await CheckGroupLimitAsync(userId);
            if (limitError != null)
                return limitError;

            var groupCode = await GenerateUniqueGroupCode();

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

        /// <summary>
        /// Joins a group by code, restoring a soft-deleted membership when someone comes back.
        /// The password check runs before the restore branch on purpose - a returning member
        /// gets no shortcut past it (that ordering bug shipped once). Rejoiners always come back
        /// as plain Members regardless of the role they once held.
        /// </summary>
        public async Task<Result<GroupDto>> JoinGroupAsync(JoinGroupDto joinGroupDto, Guid userId)
        {
            var code = joinGroupDto.GroupCode?.Trim().ToUpperInvariant() ?? string.Empty;

            if (!_codeGenerator.IsValid(code))
                return Error.NotFound("Invalid group code");

            var groupData = await _context.Groups
                .Where(g => g.GroupCode == code)
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


            // IgnoreQueryFilters: a previously removed membership is soft-deleted and the restore
            // branch below needs to find it.
            var existingMember = await _context.GroupMembers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(gm => gm.GroupId == groupData.Id && gm.UserId == userId);

            if (existingMember != null && !existingMember.IsDeleted)
            {
                return Error.Conflict("You are already a member of this group");
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

            var limitError = await CheckGroupLimitAsync(userId);
            if (limitError != null)
                return limitError;

            var rejoin = existingMember != null;

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
                userId, rejoin ? "rejoined" : "joined", groupData.Id);

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

        /// <summary>
        /// Every group the caller belongs to, with member counts computed in SQL rather than by
        /// loading members.
        /// </summary>
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
                    MemberCount = gm.Group.Members.Count,
                    UserRole = (GroupRole)gm.RoleId,
                    JoinedAt = gm.CreatedAt,
                    CreatedAt = gm.Group.CreatedAt,
                }).ToListAsync();

            return groups;
        }

        /// <summary>
        /// The group's header info plus its member list, for members only.
        /// </summary>
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
                    ProfilePicturePath = gm.User.ProfilePicturePath,
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

        /// <summary>
        /// Renames the group or changes its password, Manager and above only. The password field
        /// has three states: null keeps the current one, empty string removes protection, and
        /// anything else becomes the new hash.
        /// </summary>
        public async Task<Result<GroupDto>> UpdateGroupAsync(Guid groupId, UpdateGroupDto updateGroupDto, Guid userId)
        {
            var group = await _context.Groups.FindAsync(groupId);

            if (group == null)
                return Error.NotFound("Group not found");

            var callerRole = await GetUserRoleAsync(groupId, userId);

            if (callerRole == null)
                return Error.Forbidden("You are not a member of this group");

            if (callerRole < GroupRole.Manager)
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
                UserRole = callerRole.Value,
            };
        }

        /// <summary>
        /// Changes a member's role under the rank rules: you cannot touch your own role, you can
        /// only manage members strictly below you, you cannot hand out a role at or above your
        /// own, and Owner is never assignable here - ownership moves through
        /// <see cref="TransferOwnershipAsync"/> only.
        /// </summary>
        public async Task<Result<GroupMemberDto>> ChangeUserRoleAsync(
            Guid groupId, Guid memberId, ChangeRoleDto changeRoleDto, Guid userId)
        {
            _logger.LogInformation("User {UserId} changing role for member {MemberId} in group {GroupId}",
                userId, memberId, groupId);

            var callerRole = await GetUserRoleAsync(groupId, userId);

            if (callerRole == null)
                return Error.Forbidden("You are not a member of this group");

            if (callerRole < GroupRole.Manager)
                return Error.Forbidden("Only Owner or Manager can change roles");

            if (memberId == userId)
                return Error.BadRequest("You cannot change your own role");

            if (changeRoleDto.NewRole == GroupRole.Owner)
                return Error.BadRequest("Ownership cannot be assigned through a role change");

            if (!Enum.IsDefined(changeRoleDto.NewRole))
                return Error.BadRequest("Invalid role");

            var row = await _context.GroupMembers
                .Where(gm => gm.GroupId == groupId && gm.UserId == memberId)
                .Select(gm => new
                {
                    Membership = gm,
                    gm.User.UserName,
                    gm.User.Email,
                    gm.User.ProfilePicturePath
                })
                .FirstOrDefaultAsync();

            if (row == null)
                return Error.NotFound("Member not found in this group");

            var targetMembership = row.Membership;

            var targetRole = (GroupRole)targetMembership.RoleId;
            if (targetRole >= callerRole)
                return Error.Forbidden("You can only change roles of members below your own role");

            if (changeRoleDto.NewRole >= callerRole)
                return Error.Forbidden("You cannot assign a role at or above your own");


            targetMembership.RoleId = (int)changeRoleDto.NewRole;
            targetMembership.UpdatedBy = userId;
     
            await _context.SaveChangesAsync();

            _logger.LogInformation("Role changed for member {MemberId} to {NewRole}", memberId, changeRoleDto.NewRole);

            return new GroupMemberDto
            {
                UserId = targetMembership.UserId,
                UserName = row.UserName,
                Email = row.Email,
                ProfilePicturePath = row.ProfilePicturePath,
                Role = changeRoleDto.NewRole
            };
        }

        /// <summary>
        /// Moves ownership to another member in one save: the new owner becomes Owner, the old
        /// one steps down to Manager, and the group row's OwnerId follows. Owner-only.
        /// </summary>
        public async Task<Result> TransferOwnershipAsync(Guid groupId, Guid newOwnerId, Guid userId)
        {
            if (newOwnerId == userId)
                return Error.BadRequest("Can't transfer ownership to yourself");

            var callerRole = await GetUserRoleAsync(groupId, userId);

            if (callerRole == null)
                return Error.Forbidden("You are not a member of this group");

            if (callerRole < GroupRole.Owner)
                return Error.Forbidden("Only the owner can transfer ownership");

            var newOwnerMembership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == newOwnerId);
            if (newOwnerMembership == null)
                return Error.NotFound("New owner must be a group member");

            var currentOwnerMembership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == userId);
            var group = await _context.Groups.FindAsync(groupId);

            group!.OwnerId = newOwnerId;
            newOwnerMembership.RoleId = (int)GroupRole.Owner;
            currentOwnerMembership!.RoleId = (int)GroupRole.Manager;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Ownership of group {GroupId} transferred from {UserId} to {NewOwnerId}",
                groupId, userId, newOwnerId);
            return Result.Success();
        }

        /// <summary>
        /// Soft-deletes the group and everything under it (tasks, comments, attachments and
        /// their notifications) in one transaction. Owner-only, and only once every other member
        /// has been removed. The ExecuteUpdate cascades set UpdatedAt by hand because bulk
        /// updates bypass the SaveChanges stamping override.
        /// </summary>
        public async Task<Result> DeleteGroupAsync(Guid groupId, Guid userId)
        {
            var callerRole = await GetUserRoleAsync(groupId, userId);

            if (callerRole == null)
                return Error.Forbidden("You are not a member of this group");

            if (callerRole < GroupRole.Owner)
                return Error.Forbidden("Only the owner can delete the group");

            var memberCount = await _context.GroupMembers
                .CountAsync(gm => gm.GroupId == groupId);


            // Keeps the cascade at single-membership: only the owner's row needs soft-deleting
            // Relaxing this requires adding a GroupMembers ExecuteUpdate to the cascade
            if (memberCount > 1)
                return Error.BadRequest("Remove all other members before deleting the group");

            var group = await _context.Groups.FindAsync(groupId);
            if (group == null)
                return Error.NotFound("Group not found");

            var now = DateTime.UtcNow;

            var ownerMembership = await _context.GroupMembers
                .FirstAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            await using var transaction = await _context.BeginTransactionAsync();

            await _context.Notifications
                .IgnoreQueryFilters()
                .Where(n => _context.Tasks.Any(t => t.Id == n.RelatedEntityId && t.GroupId == groupId)
                            && !n.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.IsDeleted, true)
                    .SetProperty(n => n.DeletedAt, now)
                    .SetProperty(n => n.DeletedBy, userId)
                    .SetProperty(n => n.UpdatedAt, now));

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
                    .SetProperty(a => a.UpdatedAt, now));


            await _context.Tasks
                .IgnoreQueryFilters()
                .Where(t => t.GroupId == groupId && !t.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.IsDeleted, true)
                    .SetProperty(t => t.DeletedAt, now)
                    .SetProperty(t => t.DeletedBy, userId)
                    .SetProperty(t => t.UpdatedAt, now));

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

        /// <summary>
        /// Removes a member (soft delete), Manager and above only, and only for members ranked
        /// strictly below the caller. The owner can never be removed, and removing yourself is
        /// redirected to <see cref="LeaveGroupAsync"/>.
        /// </summary>
        public async Task<Result> RemoveUserFromGroupAsync(Guid groupId, Guid memberId, Guid userId)
        {
            _logger.LogInformation("User {UserId} removing member {MemberId} from group {GroupId}",
                userId, memberId, groupId);

            if (userId == memberId)
                return Error.BadRequest("Leave the group instead");

            var callerRole = await GetUserRoleAsync(groupId, userId);

            if (callerRole == null)
                return Error.Forbidden("You are not a member of this group");

            if (callerRole < GroupRole.Manager) 
                return Error.Forbidden("Only Owner or Manager can remove members");

            var targetMembership = await _context.GroupMembers
                .FirstOrDefaultAsync(gm => gm.GroupId == groupId && gm.UserId == memberId);

            if (targetMembership == null)
                return Error.NotFound("Member not found in this group");

            var targetRole = (GroupRole)targetMembership.RoleId;

            if (targetRole == GroupRole.Owner)
                return Error.BadRequest("Cannot remove the group owner");

            if (targetRole >= callerRole)
                return Error.Forbidden("You can only remove members below your own role");

            targetMembership.IsDeleted = true;
            targetMembership.DeletedAt = DateTime.UtcNow;
            targetMembership.DeletedBy = userId;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Member {MemberId} removed from group {GroupId}", memberId, groupId);

            return Result.Success();
        }

        /// <summary>
        /// The caller leaves the group (soft delete, so rejoining later restores the row). The
        /// owner cannot leave - ownership must be transferred or the group deleted instead.
        /// </summary>
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

        /// <summary>
        /// Enforces the group cap from the user's resolved plan. Returns the error to hand back,
        /// or null when the user still has room.
        /// </summary>
        private async Task<Error?> CheckGroupLimitAsync(Guid userId)
        {
            // This used to read User.MaxGroups, a stored copy the nightly expiry job kept up to
            // date. Between a pass expiring and that job running, enforcement saw the premium
            // number while the status endpoint reported the free one. The limit is derived now,
            // so the two cannot disagree.
            var entitlements = await _entitlements.GetEntitlementsAsync(userId);
            if (entitlements.IsFailure)
                return entitlements.Error!;

            var maxGroups = entitlements.Value!.MaxGroups;

            var currentGroupCount = await _entitlements.GetGroupCountAsync(userId);

            return currentGroupCount >= maxGroups
                ? Error.Forbidden($"You've reached your limit of {maxGroups} Trees. Upgrade to Premium for more.")
                : null;
        }


        /// <summary>
        /// Draws join codes until one is unused. Five tries against a 32^8 space means a
        /// collision streak signals something genuinely wrong, so it throws rather than loops.
        /// </summary>
        private async Task<string> GenerateUniqueGroupCode()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var code = _codeGenerator.Generate();
                if (!await _context.Groups.AnyAsync(g => g.GroupCode == code))
                    return code;
            }
            throw new InvalidOperationException("Could not generate a unique group code");
        }

    }
}