using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Plantitask.Core.Common;
using Plantitask.Core.Common.Interfaces;
using Plantitask.Core.Entities;
using Plantitask.Core.Entities.Lookups;
using Plantitask.Core.Interfaces;
using System.Linq.Expressions;

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

    public DbSet<PlanVersion> PlanVersions { get; set; }
    public DbSet<UserPlanGrant> UserPlanGrants { get; set; }

    public DbSet<TaskStatusLookup> TaskStatuses { get; set; }
    public DbSet<TaskPriorityLookup> TaskPriorities { get; set; }
    public DbSet<TaskComment> TaskComments { get; set; }
    public DbSet<GroupRoleLookup> GroupRoles { get; set; }
    public DbSet<PlanLookup> Plans { get; set; }

    public DbSet<Notification> Notifications { get; set; }

    public DbSet<NotificationPreference> NotificationPreferences { get; set; }
    public DbSet<NotificationDigestLog> NotificationDigestLogs { get; set; }
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }


    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    public void ClearChangeTracker() => ChangeTracker.Clear();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => Database.BeginTransactionAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        base.OnModelCreating(modelBuilder);
        modelBuilder.HasPostgresExtension("pg_trgm");

        //.Where(e => !e.IsDeleted) Building Logical Expression Tree

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {

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
            entity.Property(e => e.ProfilePicturePath).HasMaxLength(500);

        });

        modelBuilder.Entity<PlanLookup>(entity =>
        {
            entity.HasKey(e => e.Id);

            // The id is assigned from PlanTier, never generated.
            entity.Property(e => e.Id).ValueGeneratedNever();

            entity.HasIndex(e => e.Name).IsUnique();

            entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Color).HasMaxLength(20);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<PlanVersion>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.PlanId, e.Version }).IsUnique();

            // Resolution always filters on published and effective, so index the way it reads.
            entity.HasIndex(e => new { e.PlanId, e.PublishedAt, e.EffectiveFrom });

            entity.HasOne(e => e.Plan)
                .WithMany()
                .HasForeignKey(e => e.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserPlanGrant>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => new { e.UserId, e.StartsAt, e.EndsAt });

            // Serves AnyGrantForPayPalRefAsync, which looks across every grant open or closed
            // because a captured order must never be granted twice even after it expires. The
            // partial index below cannot serve that query: a partial index only applies to
            // queries that repeat its predicate.
            entity.HasIndex(e => e.PayPalRef);

            // At most one open grant per PayPal reference. This is what makes a redelivered
            // ACTIVATED webhook harmless: the second insert cannot land. Closed grants are
            // excluded so re-subscribing on the same id later still works.
            //
            // Named explicitly. HasIndex is keyed on the property set, so a second unnamed call
            // over PayPalRef would reconfigure the index above rather than add one.
            entity.HasIndex(e => e.PayPalRef, "IX_UserPlanGrants_PayPalRef_Open")
                .IsUnique()
                .HasFilter("\"EndsAt\" IS NULL AND \"PayPalRef\" IS NOT NULL");

            entity.Property(e => e.Source).HasMaxLength(50).IsRequired();
            entity.Property(e => e.PayPalRef).HasMaxLength(200);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PlanVersion)
                .WithMany()
                .HasForeignKey(e => e.PlanVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Group>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasIndex(e => e.GroupCode).IsUnique();

            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.GroupCode).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);

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
            entity.HasIndex(e => e.Title)
                  .HasMethod("gin")
                  .HasOperators("gin_trgm_ops");

          

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

            // The storage quota sums FileSize per uploader on every upload. It seeks rather than
            // scans because the Uploader relationship below already indexes CreatedBy, so there
            // is deliberately no explicit HasIndex here to duplicate it.

            // AttachmentPurgeJob's worklist. Partial so the index holds only rows that still owe
            // a file deletion, which in the steady state is none of them. Without it the job's
            // every-fifteen-minutes check sequentially scans every attachment ever uploaded to
            // prove there is nothing to do, forever. DeletedAt is the indexed column so the
            // job's ORDER BY is served too and the sort disappears.
            entity.HasIndex(e => e.DeletedAt, "IX_TaskAttachments_PendingPurge")
                .HasFilter("\"IsDeleted\" = true AND \"FilePurgedAt\" IS NULL");

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
            entity.Property(tc => tc.Content)
                .IsRequired()
                .HasMaxLength(2000);

            entity.HasOne(tc => tc.Task)
                .WithMany(t => t.Comments)
                .HasForeignKey(tc => tc.TaskId)
                .OnDelete(DeleteBehavior.Restrict);


            entity.HasOne(tc => tc.Author)
                .WithMany()
                .HasForeignKey(tc => tc.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(tc => tc.TaskId);
            entity.HasIndex(tc => tc.CreatedBy);
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
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired();


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
                new TaskPriorityLookup { Id = 1, Name = "Low", DisplayName = "Low", Description = "Low priority task", Color = "#6c757d", DisplayOrder = 1, IsActive = true },
                new TaskPriorityLookup { Id = 2, Name = "Medium", DisplayName = "Medium", Description = "Medium priority task", Color = "#0dcaf0", DisplayOrder = 2, IsActive = true },
                new TaskPriorityLookup { Id = 3, Name = "High", DisplayName = "High", Description = "High priority task", Color = "#ffc107", DisplayOrder = 3, IsActive = true },
                new TaskPriorityLookup { Id = 4, Name = "Urgent", DisplayName = "Urgent", Description = "Urgent priority task", Color = "#dc3545", DisplayOrder = 4, IsActive = true }
            );
        });

        modelBuilder.Entity<GroupRoleLookup>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(200);

            entity.HasData(
                new GroupRoleLookup { Id = 100, Name = "Owner", DisplayName = "Owner", Description = "Full control over the group", Color = "#dc3545", DisplayOrder = 1, IsActive = true },
                new GroupRoleLookup { Id = 75, Name = "Manager", DisplayName = "Manager", Description = "Can manage members and tasks", Color = "#ffc107", DisplayOrder = 2, IsActive = true },
                new GroupRoleLookup { Id = 50, Name = "TeamLead", DisplayName = "Team Lead", Description = "Can manage tasks", Color = "#0dcaf0", DisplayOrder = 3, IsActive = true },
                new GroupRoleLookup { Id = 25, Name = "Member", DisplayName = "Member", Description = "Can view and work on tasks", Color = "#6c757d", DisplayOrder = 4, IsActive = true }
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

            entity.HasOne(n => n.Actor)
                .WithMany()
                .HasForeignKey(n => n.ActorId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(n => new { n.UserId, n.IsRead });
            entity.HasIndex(n => n.CreatedAt);
            entity.HasIndex(n => n.Type);
        });

        modelBuilder.Entity<NotificationPreference>(entity =>
        {

            entity.HasOne(np => np.User)
                .WithMany()
                .HasForeignKey(np => np.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(np => new { np.UserId, np.Type }).IsUnique();

        });

        modelBuilder.Entity<NotificationDigestLog>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.Type, e.SentOn }).IsUnique();
            entity.HasIndex(e => e.SentOn);
        });

        modelBuilder.Entity<ProcessedWebhookEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();

            // The uniqueness is the guarantee, not an optimisation. Two concurrent deliveries
            // of the same event can both pass the read, so the constraint is what stops the
            // second one committing.
            entity.HasIndex(e => e.EventId).IsUnique();
            entity.HasIndex(e => e.CreatedAt);
        });
    }

    

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        foreach(var entry in entries)
        {
            if (entry.State == EntityState.Added && entry.Entity is IEntity entity)
            {
                entity.CreatedAt = DateTime.UtcNow;
            }

            if (entry.State == EntityState.Modified && entry.Entity is IUpdatable updatable)
            {
                updatable.UpdatedAt = DateTime.UtcNow;
            }
        }
        return await base.SaveChangesAsync(cancellationToken);
    }
}
