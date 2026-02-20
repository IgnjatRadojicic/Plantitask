using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Core.Entities;
using TaskManagement.Core.Entities.Lookups;



namespace TaskManagement.Core.Interfaces

{
    public interface IApplicationDbContext
    {

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

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
