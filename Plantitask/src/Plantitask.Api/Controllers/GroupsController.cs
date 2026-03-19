using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plantitask.Api.Extensions;
using Plantitask.Core.DTO.Groups;
using Plantitask.Core.Interfaces;

namespace Plantitask.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("general")]
    [Produces("application/json")]
    public class GroupsController : BaseApiController
    {
        private readonly IGroupService _groupService;
        private readonly IAuditService _auditService;
        private readonly ILogger<GroupsController> _logger;

        public GroupsController(
            IGroupService groupService,
            IAuditService auditService,
            ILogger<GroupsController> logger)
        {
            _groupService = groupService;
            _auditService = auditService;
            _logger = logger;
        }

        [HttpPost]
        [ProducesResponseType(typeof(GroupDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto createGroupDto)
        {
            var userId = GetUserId();
            var result = await _groupService.CreateGroupAsync(createGroupDto, userId);

            if (result.IsFailure)
                return result.ToActionResult();

            var group = result.Value!;

            await LogAuditAsync(
                _auditService,
                entityType: "Group",
                entityId: group.Id,
                action: "Created",
                groupId: group.Id);

            return Ok(group);
        }

        [HttpPost("join")]
        [ProducesResponseType(typeof(GroupDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> JoinGroup([FromBody] JoinGroupDto joinGroupDto)
        {
            var userId = GetUserId();
            var result = await _groupService.JoinGroupAsync(joinGroupDto, userId);

            if (result.IsFailure)
                return result.ToActionResult();

            var group = result.Value!;

            await LogAuditAsync(
                _auditService,
                entityType: "GroupMember",
                entityId: userId,
                action: "JoinedGroup",
                groupId: group.Id);

            return Ok(group);
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<GroupDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetUserGroups()
        {
            var userId = GetUserId();
            var result = await _groupService.GetUserGroupsAsync(userId);
            return result.ToActionResult();
        }

        [HttpGet("{groupId}")]
        [ProducesResponseType(typeof(GroupDetailsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetGroupDetails(Guid groupId)
        {
            var userId = GetUserId();
            var result = await _groupService.GetGroupDetailsAsync(groupId, userId);
            return result.ToActionResult();
        }

        [HttpPut("{groupId}")]
        [ProducesResponseType(typeof(GroupDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> UpdateGroup(Guid groupId, [FromBody] UpdateGroupDto updateGroupDto)
        {
            var userId = GetUserId();
            var result = await _groupService.UpdateGroupAsync(groupId, updateGroupDto, userId);

            if (result.IsFailure)
                return result.ToActionResult();

            var group = result.Value!;

            await LogAuditAsync(
                _auditService,
                entityType: "Group",
                entityId: groupId,
                action: "Updated",
                groupId: groupId);

            return Ok(group);
        }

        [HttpPut("{groupId}/members/{memberId}/role")]
        [ProducesResponseType(typeof(GroupMemberDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ChangeUserRole(
            Guid groupId, Guid memberId, [FromBody] ChangeRoleDto changeRoleDto)
        {
            var userId = GetUserId();
            var result = await _groupService.ChangeUserRoleAsync(groupId, memberId, changeRoleDto, userId);

            if (result.IsFailure)
                return result.ToActionResult();

            var member = result.Value!;

            await LogAuditAsync(
                _auditService,
                entityType: "GroupMember",
                entityId: memberId,
                action: "RoleChanged",
                groupId: groupId,
                propertyName: "Role",
                oldValue: null,
                newValue: changeRoleDto.NewRole.ToString());

            return Ok(member);
        }

        [HttpDelete("{groupId}/members/{memberId}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> RemoveUserFromGroup(Guid groupId, Guid memberId)
        {
            var userId = GetUserId();
            var result = await _groupService.RemoveUserFromGroupAsync(groupId, memberId, userId);

            if (result.IsFailure)
                return result.ToActionResult();

            await LogAuditAsync(
                _auditService,
                entityType: "GroupMember",
                entityId: memberId,
                action: "RemovedFromGroup",
                groupId: groupId);

            return Ok(new { message = "Member removed successfully" });
        }

        [HttpPost("{groupId}/leave")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> LeaveGroup(Guid groupId)
        {
            var userId = GetUserId();
            var result = await _groupService.LeaveGroupAsync(groupId, userId);

            if (result.IsFailure)
                return result.ToActionResult();

            await LogAuditAsync(
                _auditService,
                entityType: "GroupMember",
                entityId: userId,
                action: "LeftGroup",
                groupId: groupId);

            return Ok(new { message = "Left group successfully" });
        }
    }
}