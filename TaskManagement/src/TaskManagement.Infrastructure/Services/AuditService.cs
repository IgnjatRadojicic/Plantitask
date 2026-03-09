
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskManagement.Core.DTO.Audit;
using TaskManagement.Core.Entities;
using TaskManagement.Core.Interfaces;

namespace TaskManagement.Infrastructure.Services
{
    public class AuditService : IAuditService
    {
        private readonly IApplicationDbContext _context;
        private readonly ILogger<AuditService> _logger;

        public AuditService(IApplicationDbContext context, ILogger<AuditService> logger)
        {
            _context = context;
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
            string? reason = null
            )
        {

                var user = await _context.Users.FindAsync(userId);
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

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                "Audit log created: {EntityType} {EntityId} - {Action} by user {UserId} in group {GroupId}",
                entityType, entityId, action, userId, groupId);
            
        }
        public async Task<List<AuditLogDto>> GetEntityHistoryAsync(string entityType, Guid entityId, Guid requestingUserId)
        {
                Guid? groupId = await GetEntityGroupdIdAsync(entityType, entityId);

                if(groupId.HasValue)
                {
                    var isMember = await _context.GroupMembers
                        .AnyAsync(gm => gm.GroupId == groupId.Value && gm.UserId == requestingUserId);
                    if (!isMember)
                    {
                        throw new UnauthorizedAccessException("You must be a member of this group to view its audit history");
                    }
                }
                var logs = await _context.AuditLogs
                    .Where(a => a.EntityType == entityType && a.EntityId == entityId)
                    .OrderByDescending(a => a.CreatedAt)
                    .Select(a => new AuditLogDto
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
                    })
                    .ToListAsync();

                return logs;
            
        }

        public async Task<List<AuditLogDto>> GetGroupHistoryAsync(Guid groupId, Guid requestingUserId, int pageNumber = 1, int pageSize = 50)
        {
                var membership = await _context.GroupMembers
                    .AnyAsync(gm => gm.GroupId == groupId && gm.UserId == requestingUserId);

                if (!membership)
                {
                    throw new UnauthorizedAccessException("You must be a member of this group to view its audit history");
                }
                var logs = await _context.AuditLogs
                    .Where(a => a.GroupId == groupId)
                    .OrderByDescending(a => a.CreatedAt)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(a => new AuditLogDto
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
                    })
                    .ToListAsync();

                return logs;
            
        }

        public async Task<List<AuditLogDto>> GetUserHistoryAsync(Guid userId, Guid requestingUserId, int pageNumber = 1, int pageSize = 50)
        {
                var userGroupIds = await _context.GroupMembers
                    .Where(gm => gm.UserId == requestingUserId)
                    .Select(gm => gm.GroupId)
                    .ToListAsync();

                var logs = await _context.AuditLogs
                    .Where(a => a.UserId == userId &&
                               (a.GroupId == null || userGroupIds.Contains(a.GroupId.Value)))
                    .OrderByDescending(a => a.CreatedAt)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .Select(a => new AuditLogDto
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
                    })
                    .ToListAsync();

                return logs;
            
        }


        public async Task<List<AuditLogDto>> GetTaskHistoryAsync(Guid taskId, Guid requestingUserId)
        {
            return await GetEntityHistoryAsync("TaskItem", taskId, requestingUserId);
        }




        private async Task<Guid?> GetEntityGroupdIdAsync(string entityType, Guid entityId) {

            return entityType switch
            {
                "TaskItem" => await _context.Tasks
                    .Where(t => t.Id == entityId)
                    .Select(t => (Guid?)t.GroupId)
                    .FirstOrDefaultAsync(),

                "Group" => entityId,

                "GroupMember" => await _context.GroupMembers
                    .Where(gm => gm.Id == entityId)
                    .Select(gm => (Guid?)gm.GroupId)
                    .FirstOrDefaultAsync(),

                _ => null
            };
        }


    }
}










