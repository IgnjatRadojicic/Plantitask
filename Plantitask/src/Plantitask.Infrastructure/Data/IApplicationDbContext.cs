using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Plantitask.Core.Entities;
using Plantitask.Core.Entities.Lookups;

namespace Plantitask.Infrastructure.Data
{
    public interface IApplicationDbContext
    {
        ChangeTracker ChangeTracker { get; }
        DbSet<User> Users { get; set; }
        DbSet<Group> Groups { get; set; }
        DbSet<GroupMember> GroupMembers { get; set; }
        DbSet<TaskItem> Tasks { get; set; }
        DbSet<TaskAttachment> TaskAttachments { get; set; }
        DbSet<TaskComment> TaskComments { get; set; }


        DbSet<PlanVersion> PlanVersions { get; set; }
        DbSet<UserPlanGrant> UserPlanGrants { get; set; }

        DbSet<TaskStatusLookup> TaskStatuses { get; set; }
        DbSet<TaskPriorityLookup> TaskPriorities { get; set; }
        DbSet<GroupRoleLookup> GroupRoles { get; set; }
        DbSet<PlanLookup> Plans { get; set; }
        DbSet<Notification> Notifications { get; set; }
        DbSet<NotificationPreference> NotificationPreferences { get; set; }
        DbSet<NotificationDigestLog> NotificationDigestLogs { get; set; }
        DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents { get; set; }
        DbSet<AuditLog> AuditLogs { get; set; }
        DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

        void ClearChangeTracker();

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

        Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    }
}
