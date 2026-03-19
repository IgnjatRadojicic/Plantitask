using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Common;
using Plantitask.Core.DTO.Audit;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Infrastructure.Services
{
    public class AuditService : IAuditService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _factory;
        private readonly ILogger<AuditService> _logger;

        public AuditService(
            IDbContextFactory<ApplicationDbContext> factory,
            ILogger<AuditService> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task LogAsync(
            string entityType,
            Guid entityId,
            string action,
            Guid userId,
            Guid? groupId = null,
            string? ipAddress = null,
            string? userAgent = null,
            string? propertyName = null,
            string? oldValue = null,
            string? newValue = null,
            string? reason = null)
        {
            await using var context = await _factory.CreateDbContextAsync();

            var user = await context.Users.FindAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("Cannot create audit log for non-existent user {UserId}", userId);
                return;
            }

            var auditLog = new AuditLog
            {
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                PropertyName = propertyName,
                OldValue = oldValue,
                NewValue = newValue,
                UserId = userId,
                UserName = user.UserName,
                UserEmail = user.Email,
                GroupId = groupId,
                Reason = reason,
                IpAddress = ipAddress ?? "Unknown",
                UserAgent = userAgent ?? "Unknown",
                CreatedAt = DateTime.UtcNow
            };

            context.AuditLogs.Add(auditLog);
            await context.SaveChangesAsync();

            _logger.LogInformation(
                "Audit log created: {EntityType} {EntityId} - {Action} by user {UserId} in group {GroupId}",
                entityType, entityId, action, userId, groupId);
        }

        public async Task<Result<List<AuditLogDto>>> GetEntityHistoryAsync(string entityType, Guid entityId, Guid requestingUserId)
        {
            await using var context = await _factory.CreateDbContextAsync();

            Guid? groupId = await GetEntityGroupIdAsync(context, entityType, entityId);

            if (groupId.HasValue)
            {
                var isMember = await context.GroupMembers
                    .AnyAsync(gm => gm.GroupId == groupId.Value && gm.UserId == requestingUserId);

                if (!isMember)
                    return Error.Forbidden("You must be a member of this group to view its audit history");
            }

            var logs = await context.AuditLogs
                .Where(a => a.EntityType == entityType && a.EntityId == entityId)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => MapToDto(a))
                .ToListAsync();

            return logs;
        }

        public async Task<Result<List<AuditLogDto>>> GetGroupHistoryAsync(Guid groupId, Guid requestingUserId, int pageNumber = 1, int pageSize = 50)
        {
            await using var context = await _factory.CreateDbContextAsync();

            var isMember = await context.GroupMembers
                .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == requestingUserId);

            if (!isMember)
                return Error.Forbidden("You must be a member of this group to view its audit history");

            var logs = await context.AuditLogs
                .Where(a => a.GroupId == groupId)
                .OrderByDescending(a => a.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(a => MapToDto(a))
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
                .Select(a => MapToDto(a))
                .ToListAsync();

            return logs;
        }

        public async Task<Result<List<AuditLogDto>>> GetTaskHistoryAsync(Guid taskId, Guid requestingUserId)
        {
            return await GetEntityHistoryAsync("TaskItem", taskId, requestingUserId);
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

        private static AuditLogDto MapToDto(AuditLog a)
        {
            return new AuditLogDto
            {
                Id = a.Id,
                EntityType = a.EntityType,
                EntityId = a.EntityId,
                Action = a.Action,
                PropertyName = a.PropertyName,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                UserId = a.UserId,
                UserName = a.UserName,
                UserEmail = a.UserEmail,
                Timestamp = a.CreatedAt,
                Reason = a.Reason,
                IpAddress = a.IpAddress
            };
        }
    }
}