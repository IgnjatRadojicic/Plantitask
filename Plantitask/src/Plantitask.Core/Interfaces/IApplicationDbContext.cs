using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Plantitask.Core.Entities;
using Plantitask.Core.Entities.Lookups;



namespace Plantitask.Core.Interfaces

{
    public interface IApplicationDbContext
    {
        Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }
        DbSet<User> Users { get; set; }
        DbSet<Group> Groups { get; set; }
        DbSet<GroupMember> GroupMembers { get; set; }
        DbSet<TaskItem> Tasks { get; set; }
        DbSet<TaskAttachment> TaskAttachments { get; set; }
        DbSet<TaskComment> TaskComments { get; set; }


        DbSet<TaskStatusLookup> TaskStatuses { get; set; }
        DbSet<TaskPriorityLookup> TaskPriorities { get; set; }
        DbSet<GroupRoleLookup> GroupRoles { get; set; }
        DbSet<Notification> Notifications { get; set; }
        DbSet<NotificationPreference> NotificationPreferences { get; set; }
        DbSet<AuditLog> AuditLogs { get; set; }
        DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

        void ClearChangeTracker();

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
