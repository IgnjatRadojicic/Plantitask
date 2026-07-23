using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Audit;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Projections;
using Plantitask.Infrastructure.Data;


namespace Plantitask.Infrastructure.Services
{
    public class AuditService : IAuditService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _factory;
        private readonly ILogger<AuditService> _logger;
        private readonly IGroupService _groupService;

        public AuditService(
            IDbContextFactory<ApplicationDbContext> factory,
            ILogger<AuditService> logger,
            IGroupService groupService)
        {
            _groupService = groupService;
            _factory = factory;
            _logger = logger;
        }

        public async Task LogAsync(CreateAuditLogRequest request)
        {

            try
            {
                await using var context = await _factory.CreateDbContextAsync();

                var auditLog = new AuditLog
                {
                    EntityType = request.EntityType,
                    EntityId = request.EntityId,
                    Action = request.Action,
                    UserId = request.UserId,
                    UserName = request.UserName,
                    UserEmail = request.UserEmail,
                    GroupId = request.GroupId,
                    IpAddress = request.IpAddress ?? "Unknown",
                    UserAgent = request.UserAgent ?? "Unknown",
                    PropertyName = request.PropertyName,
                    OldValue = request.OldValue,
                    NewValue = request.NewValue,
                    Reason = request.Reason
                };

                context.AuditLogs.Add(auditLog);
                await context.SaveChangesAsync();

                _logger.LogInformation(
                    "Audit log created: {EntityType} {EntityId} - {Action} by user {UserId} in group {GroupId}",
                    request.EntityType, request.EntityId, request.Action, request.UserId, request.GroupId);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to create audit log for {EntityType} {EntityId} - {Action}",
                    request.EntityType, request.EntityId, request.Action);
            }
        }

        public async Task<Result<List<AuditLogDto>>> GetEntityHistoryAsync(string entityType, Guid entityId, Guid requestingUserId, int pageNumber = 1, int pageSize = 50)
        {
            await using var context = await _factory.CreateDbContextAsync();

            Guid? groupId = await GetEntityGroupIdAsync(context, entityType, entityId);

            if (groupId.HasValue)
            {
                var isMember = await _groupService.IsUserMemberAsync(groupId.Value, requestingUserId);

                if (!isMember)
                    return Error.Forbidden("You must be a member of this group to view its audit history");
            }

            var logs = await context.AuditLogs
                .Where(a => a.EntityType == entityType && a.EntityId == entityId)
                .OrderByDescending(a => a.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(AuditLogProjections.ToAuditLogDto)
                .ToListAsync();

            return logs;
        }

        public async Task<Result<List<AuditLogDto>>> GetGroupHistoryAsync(Guid groupId, Guid requestingUserId, int pageNumber = 1, int pageSize = 50)
        {
            await using var context = await _factory.CreateDbContextAsync();

            var isMember = await _groupService.IsUserMemberAsync(groupId, requestingUserId);

            if (!isMember)
                return Error.Forbidden("You must be a member of this group to view its audit history");

            var logs = await context.AuditLogs
                .Where(a => a.GroupId == groupId)
                .OrderByDescending(a => a.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(AuditLogProjections.ToAuditLogDto)
                .ToListAsync();

            return logs;
        }

        public async Task<Result<List<AuditLogDto>>> GetUserHistoryAsync(Guid userId, Guid requestingUserId, int pageNumber = 1, int pageSize = 50)
        {
            await using var context = await _factory.CreateDbContextAsync();

            var userGroupIds = await context.GroupMembers
                .Where(gm => gm.UserId == requestingUserId)
                .Select(gm => gm.GroupId)
                .ToListAsync();

            var logs = await context.AuditLogs
                .Where(a => a.UserId == userId &&
                           (a.GroupId == null || userGroupIds.Contains(a.GroupId.Value)))
                .OrderByDescending(a => a.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(AuditLogProjections.ToAuditLogDto)
                .ToListAsync();

            return logs;
        }


        private static async Task<Guid?> GetEntityGroupIdAsync(ApplicationDbContext context, string entityType, Guid entityId)
        {
            return entityType switch
            {
                "TaskItem" => await context.Tasks
                    .Where(t => t.Id == entityId)
                    .Select(t => (Guid?)t.GroupId)
                    .FirstOrDefaultAsync(),

                "Group" => entityId,

                "GroupMember" => await context.GroupMembers
                    .Where(gm => gm.Id == entityId)
                    .Select(gm => (Guid?)gm.GroupId)
                    .FirstOrDefaultAsync(),

                _ => null
            };
        }

    }
}