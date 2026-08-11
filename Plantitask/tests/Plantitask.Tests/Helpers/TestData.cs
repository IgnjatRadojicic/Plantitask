using Microsoft.EntityFrameworkCore;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Tests.Helpers
{
    /// <summary>
    /// Well known ids for the seeded world. Fixed rather than generated so a failing assertion
    /// names something recognisable and so a test can reference an actor it did not create.
    /// </summary>
    public static class TestIds
    {
        public static readonly Guid LeadId = new("aaaaaaaa-0000-0000-0000-000000000001");
        public static readonly Guid MemberId = new("aaaaaaaa-0000-0000-0000-000000000002");
        public static readonly Guid OutsiderId = new("aaaaaaaa-0000-0000-0000-000000000003");
        public static readonly Guid OtherLeadId = new("aaaaaaaa-0000-0000-0000-000000000004");

        public static readonly Guid GroupId = new("11111111-0000-0000-0000-000000000001");
        public static readonly Guid OtherGroupId = new("11111111-0000-0000-0000-000000000002");
    }

    /// <summary>
    /// Factories for unsaved entities. Every one sets exactly the columns the database will not
    /// let us leave out so a test never fails on a constraint it was not written to exercise.
    /// </summary>
    public static class TestData
    {
        public static User User(Guid id, string name) => new()
        {
            Id = id,
            UserName = name,
            Email = $"{name}@example.com",
            PasswordHash = "not-a-real-hash"
        };

        public static Group Group(Guid id, string name, string code, Guid ownerId) => new()
        {
            Id = id,
            Name = name,
            GroupCode = code,
            OwnerId = ownerId,
            CreatedBy = ownerId
        };

        public static GroupMember Membership(Guid groupId, Guid userId, GroupRole role) => new()
        {
            GroupId = groupId,
            UserId = userId,
            RoleId = (int)role,
            CreatedBy = userId
        };

        public static TaskItem Task(
            Guid groupId,
            Guid createdBy,
            string title = "Seeded task",
            TaskStatusItem status = TaskStatusItem.NotStarted,
            TaskPriority priority = TaskPriority.Medium,
            Guid? assignedTo = null,
            DateTime? dueDate = null,
            int displayOrder = 0,
            Guid? id = null) => new()
            {
                Id = id ?? Guid.NewGuid(),
                Title = title,
                Description = "Seeded description",
                GroupId = groupId,
                StatusId = (int)status,
                PriorityId = (int)priority,
                AssignedToId = assignedTo,
                DueDate = dueDate,
                DisplayOrder = displayOrder,
                CreatedBy = createdBy
            };
    }

    public static class SeedExtensions
    {
        /// <summary>
        /// The arrangement every service test starts from: one group the caller belongs to and a
        /// second group they do not. The second group is what makes a cross tenant denial test
        /// meaningful, and it is also what makes a group filter assertable at all since there is
        /// finally other data for a query to wrongly return.
        /// </summary>
        public static async Task SeedWorldAsync(this ApplicationDbContext db)
        {
            db.Users.AddRange(
                TestData.User(TestIds.LeadId, "lead"),
                TestData.User(TestIds.MemberId, "member"),
                TestData.User(TestIds.OutsiderId, "outsider"),
                TestData.User(TestIds.OtherLeadId, "otherlead"));

            db.Groups.AddRange(
                TestData.Group(TestIds.GroupId, "Dev Team", "DEV12345", TestIds.LeadId),
                TestData.Group(TestIds.OtherGroupId, "Other Team", "OTH12345", TestIds.OtherLeadId));

            db.GroupMembers.AddRange(
                TestData.Membership(TestIds.GroupId, TestIds.LeadId, GroupRole.TeamLead),
                TestData.Membership(TestIds.GroupId, TestIds.MemberId, GroupRole.Member),
                TestData.Membership(TestIds.OtherGroupId, TestIds.OtherLeadId, GroupRole.Owner));

            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Repoints an existing membership at another role. Rank boundary theories use this
        /// instead of reseeding so the rest of the world stays identical across cases.
        /// </summary>
        public static async Task SetRoleAsync(
            this ApplicationDbContext db, Guid userId, GroupRole role, Guid? groupId = null)
        {
            var target = groupId ?? TestIds.GroupId;

            var membership = await db.GroupMembers
                .SingleAsync(gm => gm.GroupId == target && gm.UserId == userId);

            membership.RoleId = (int)role;
            await db.SaveChangesAsync();
        }
    }
}
