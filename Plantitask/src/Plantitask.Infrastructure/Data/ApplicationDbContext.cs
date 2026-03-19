using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Plantitask.Core.Common;
using Plantitask.Core.Entities;
using Plantitask.Core.Entities.Lookups;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupMember> GroupMembers { get; set; }
    public DbSet<TaskItem> Tasks { get; set; }
    public DbSet<TaskAttachment> TaskAttachments { get; set; }

    public DbSet<TaskStatusLookup> TaskStatuses { get; set; }
    public DbSet<TaskPriorityLookup> TaskPriorities { get; set; }
    public DbSet<TaskComment> TaskComments { get; set; }
    public DbSet<GroupRoleLookup> GroupRoles { get; set; }

    public DbSet<Notification> Notifications { get; set; }

    public DbSet<NotificationPreference> NotificationPreferences { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }


    public void ClearChangeTracker() => ChangeTracker.Clear();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        base.OnModelCreating(modelBuilder);

        //.Where(e => !e.IsDeleted) Building Logical Expression Tree

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType == typeof(AuditLog) ||
             entityType.ClrType == typeof(PasswordResetToken))
            {
                continue;
            }

            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var property = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
                var filter = Expression.Lambda(Expression.Not(property), parameter);

                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }

            if(typeof(SelfManagedEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var property = Expression.Property(parameter, nameof(SelfManagedEntity.IsDeleted));
                var filter = Expression.Lambda(Expression.Not(property), parameter);

                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.UserName).IsUnique();

            entity.Property(e => e.UserName).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(256).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(e => e.FirstName).HasMaxLength(50);
            entity.Property(e => e.LastName).HasMaxLength(50);
            entity.Property(e => e.ProfilePictureUrl).HasMaxLength(500);

        });

        modelBuilder.Entity<Group>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.GroupCode).IsUnique();

            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.GroupCode).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(500);

            entity.HasOne(e => e.Owner)
                .WithMany(u => u.OwnedGroups)
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);  

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GroupMember>(entity =>
        {
            entity.HasKey(e => e.Id);

            
            entity.HasIndex(e => new { e.GroupId, e.UserId }).IsUnique();

            entity.HasOne(e => e.Group)
                .WithMany(g => g.Members)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                .WithMany(u => u.GroupMemberships)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Role)
                .WithMany(r => r.GroupMembers)
                .HasForeignKey(e => e.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);


        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("Tasks");

            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => e.AssignedToId);
            entity.HasIndex(e => e.StatusId);
            entity.HasIndex(e => e.PriorityId);
            entity.HasIndex(e => e.DueDate);

            entity.HasIndex(e => new { e.GroupId, e.StatusId, e.DisplayOrder });

            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(2000);

            entity.Property(e => e.DisplayOrder)
                .HasDefaultValue(0)
                .IsRequired();


            entity.HasOne(e => e.Group)
                .WithMany(g => g.Tasks)
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.AssignedTo)
                .WithMany(u => u.AssignedTasks)
                .HasForeignKey(e => e.AssignedToId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Creator)
                .WithMany(u => u.CreatedTasks)
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Status)
                .WithMany(s => s.Tasks)
                .HasForeignKey(e => e.StatusId)
                .OnDelete(DeleteBehavior.Restrict);


            entity.HasOne(e => e.Priority)
                .WithMany(p => p.Tasks)
                .HasForeignKey(e => e.PriorityId)
                .OnDelete(DeleteBehavior.Restrict);

        });

        modelBuilder.Entity<TaskAttachment>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.TaskId);

            entity.Property(e => e.FileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.FilePath).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();


            entity.HasOne(e => e.Task)
                .WithMany(t => t.Attachments)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Uploader)
                .WithMany(u => u.UploadedAttachments)
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);


        });

        modelBuilder.Entity<TaskComment>(entity =>
        {
            entity.HasQueryFilter(tc => !tc.IsDeleted);

            entity.Property(tc => tc.Content)
                .IsRequired()
                .HasMaxLength(2000);

            entity.HasOne(tc => tc.Task)
                .WithMany(t => t.Comments)
                .HasForeignKey(tc => tc.TaskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(tc => tc.User)
                .WithMany()
                .HasForeignKey(tc => tc.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(tc => tc.TaskId);
            entity.HasIndex(tc => tc.UserId);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAt);

            entity.Property(e => e.TokenHash).HasMaxLength(500).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45).IsRequired();

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);


        });

        modelBuilder.Entity<TaskStatusLookup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(200);
            entity.Property(e => e.Color).HasMaxLength(20);

            entity.HasData(
                new TaskStatusLookup { Id = 1, Name = "NotStarted", DisplayName = "Not Started", Description = "Task has not been started yet", Color = "#6c757d", DisplayOrder = 1, IsActive = true },
                new TaskStatusLookup { Id = 2, Name = "InProgress", DisplayName = "In Progress", Description = "Task is currently being worked on", Color = "#0dcaf0", DisplayOrder = 2, IsActive = true },
                new TaskStatusLookup { Id = 3, Name = "UnderReview", DisplayName = "Under Review", Description = "Task is under review", Color = "#ffc107", DisplayOrder = 3, IsActive = true },
                new TaskStatusLookup { Id = 4, Name = "Completed", DisplayName = "Completed", Description = "Task is completed", Color = "#198754", DisplayOrder = 4, IsActive = true }
            );
        });

        modelBuilder.Entity<TaskPriorityLookup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(200);
            entity.Property(e => e.Color).HasMaxLength(20);

            entity.HasData(
                new TaskPriorityLookup { Id = 1, Name = "Low", Description = "Low priority task", Color = "#6c757d", DisplayOrder = 1, IsActive = true },
                new TaskPriorityLookup { Id = 2, Name = "Medium", Description = "Medium priority task", Color = "#0dcaf0", DisplayOrder = 2, IsActive = true },
                new TaskPriorityLookup { Id = 3, Name = "High", Description = "High priority task", Color = "#ffc107", DisplayOrder = 3, IsActive = true },
                new TaskPriorityLookup { Id = 4, Name = "Urgent", Description = "Urgent priority task", Color = "#dc3545", DisplayOrder = 4, IsActive = true }
            );
        });

        modelBuilder.Entity<GroupRoleLookup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(200);

            entity.HasData(
                new GroupRoleLookup { Id = 1, Name = "Owner", DisplayName = "Owner", Description = "Full control over the group", PermissionLevel = 100, IsActive = true },
                new GroupRoleLookup { Id = 2, Name = "Manager", DisplayName = "Manager", Description = "Can manage members and tasks", PermissionLevel = 75, IsActive = true },
                new GroupRoleLookup { Id = 3, Name = "TeamLead", DisplayName = "Team Lead", Description = "Can manage tasks", PermissionLevel = 50, IsActive = true },
                new GroupRoleLookup { Id = 4, Name = "Member", DisplayName = "Member", Description = "Can view and work on tasks", PermissionLevel = 25, IsActive = true }
            );
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.EntityType);
            entity.HasIndex(e => e.EntityId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.GroupId);
            entity.HasIndex(e => e.CreatedAt);

            entity.Property(e => e.EntityType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(50).IsRequired();

            entity.Property(e => e.UserName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.UserEmail).HasMaxLength(256).IsRequired();

            entity.Property(e => e.PropertyName).HasMaxLength(100);
            entity.Property(e => e.OldValue).HasMaxLength(1000);
            entity.Property(e => e.NewValue).HasMaxLength(1000);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);  

            entity.HasOne(e => e.Group)
                .WithMany()
                .HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);  
        });

        
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasQueryFilter(n => !n.IsDeleted);

            entity.Property(n => n.Title)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(n => n.Message)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(n => n.RelatedEntityType)
                .HasMaxLength(50);

            entity.HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(n => new { n.UserId, n.IsRead }); 
            entity.HasIndex(n => n.CreatedAt);
        });

        modelBuilder.Entity<NotificationPreference>(entity =>
        {
            entity.HasQueryFilter(np => !np.IsDeleted);

            entity.HasOne(np => np.User)
                .WithMany()
                .HasForeignKey(np => np.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(np => new { np.UserId, np.Type }).IsUnique();

        }); 
    }

    

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach(var entry in entries)
        {
            if(entry.Entity is BaseEntity baseEntity)
            {
                if (entry.State == EntityState.Added)
                {
                    baseEntity.CreatedAt = DateTime.UtcNow;
                }
                else if (entry.State == EntityState.Modified)
                {
                    baseEntity.UpdatedAt = DateTime.UtcNow;
                }
            }
            else if(entry.Entity is SelfManagedEntity selfManaged)
            {
                if(entry.State == EntityState.Added)
                {
                    selfManaged.CreatedAt = DateTime.UtcNow;
                }
                else if (entry.State == EntityState.Modified)
                {
                    selfManaged.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
        return await base.SaveChangesAsync(cancellationToken);
    }
}
