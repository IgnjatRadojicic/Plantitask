using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Plantitask.Core.DTO.Audit;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Data;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class AuditServiceTests : DbTestBase
    {
        public AuditServiceTests(PostgresFixture fixture) : base(fixture) { }

        /// <summary>
        /// The real GroupService over its own context, so membership answers come from the
        /// seeded rows rather than from a stub.
        /// </summary>
        private AuditService NewSut(ApplicationDbContext contextForGroupService) => new(
            ContextFactory,
            NullLogger<AuditService>.Instance,
            new GroupService(
                contextForGroupService,
                Mock.Of<IGroupCodeGenerator>(),
                Mock.Of<IPasswordHasher>(),
                NullLogger<GroupService>.Instance));

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        private static CreateAuditLogRequest NewRequest(
            string entityType = "TaskItem",
            Guid? entityId = null,
            string action = "Created",
            Guid? userId = null,
            Guid? groupId = null,
            bool groupless = false,
            string? ipAddress = "203.0.113.7",
            string? userAgent = "xunit",
            string? propertyName = null,
            string? oldValue = null,
            string? newValue = null,
            string? reason = null) => new()
            {
                EntityType = entityType,
                EntityId = entityId ?? TaskId,
                Action = action,
                UserId = userId ?? LeadId,
                UserName = "lead",
                UserEmail = "lead@example.com",
                // `groupId ?? GroupId` alone cannot express "explicitly no group", so the
                // groupless case needs its own flag rather than a null argument.
                GroupId = groupless ? null : groupId ?? GroupId,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                PropertyName = propertyName,
                OldValue = oldValue,
                NewValue = newValue,
                Reason = reason
            };

        /// <summary>Writes an audit row straight to the database, bypassing the service.</summary>
        private async Task SeedLogAsync(
            string entityType,
            Guid entityId,
            Guid userId,
            Guid? groupId,
            string action = "Created",
            DateTime? createdAt = null)
        {
            await using var db = NewContext();

            var log = new AuditLog
            {
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                UserId = userId,
                UserName = "seeded",
                UserEmail = "seeded@example.com",
                GroupId = groupId,
                IpAddress = "198.51.100.4",
                UserAgent = "seed"
            };

            db.AuditLogs.Add(log);
            await db.SaveChangesAsync();

            // A historical CreatedAt cannot be seeded through SaveChangesAsync: the override in
            // ApplicationDbContext overwrites it with UtcNow on every Added IEntity. ExecuteUpdate
            // bypasses the change tracker and therefore the override, so it goes in afterwards.
            if (createdAt.HasValue)
            {
                await db.AuditLogs
                    .Where(a => a.Id == log.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.CreatedAt, createdAt.Value));
            }
        }

        // =====================================================================
        // LogAsync
        // =====================================================================

        [Fact]
        public async Task LogAsync_WritesEveryFieldOfTheRequest()
        {
            await SeedAsync();

            await using var groupCtx = NewContext();
            await NewSut(groupCtx).LogAsync(NewRequest(
                action: "Updated",
                propertyName: "Title",
                oldValue: "before",
                newValue: "after",
                reason: "because"));

            await using var assert = NewContext();
            var log = await assert.AuditLogs.SingleAsync();

            Assert.Equal("TaskItem", log.EntityType);
            Assert.Equal(TaskId, log.EntityId);
            Assert.Equal("Updated", log.Action);
            Assert.Equal(LeadId, log.UserId);
            Assert.Equal("lead", log.UserName);
            Assert.Equal("lead@example.com", log.UserEmail);
            Assert.Equal(GroupId, log.GroupId);
            Assert.Equal("203.0.113.7", log.IpAddress);
            Assert.Equal("xunit", log.UserAgent);
            Assert.Equal("Title", log.PropertyName);
            Assert.Equal("before", log.OldValue);
            Assert.Equal("after", log.NewValue);
            Assert.Equal("because", log.Reason);
            Assert.NotEqual(default, log.CreatedAt);
        }

        [Fact]
        public async Task LogAsync_SubstitutesUnknownForAMissingIpAndUserAgent()
        {
            await SeedAsync();

            await using var groupCtx = NewContext();
            await NewSut(groupCtx).LogAsync(NewRequest(ipAddress: null, userAgent: null));

            await using var assert = NewContext();
            var log = await assert.AuditLogs.SingleAsync();

            Assert.Equal("Unknown", log.IpAddress);
            Assert.Equal("Unknown", log.UserAgent);
        }

        [Fact]
        public async Task LogAsync_AcceptsAGrouplessEvent()
        {
            await SeedAsync();

            await using var groupCtx = NewContext();
            await NewSut(groupCtx).LogAsync(NewRequest(
                entityType: "User", entityId: LeadId, action: "Login", groupless: true));

            await using var assert = NewContext();
            var log = await assert.AuditLogs.SingleAsync();

            Assert.Null(log.GroupId);
            Assert.Equal("Login", log.Action);
        }

        /// <summary>
        /// The swallow is deliberate policy - an audit hiccup must never fail the user's
        /// mutation. A UserId with no matching row trips the foreign key, which is the cheapest
        /// way to make the write fail for real rather than by mocking something to throw.
        /// </summary>
        [Fact]
        public async Task LogAsync_SwallowsAFailedWriteInsteadOfThrowing()
        {
            await SeedAsync();

            await using var groupCtx = NewContext();

            var thrown = await Record.ExceptionAsync(() =>
                NewSut(groupCtx).LogAsync(NewRequest(userId: Guid.NewGuid())));

            Assert.Null(thrown);

            await using var assert = NewContext();
            Assert.Equal(0, await assert.AuditLogs.CountAsync());
        }

        /// <summary>
        /// The reason AuditService takes IDbContextFactory at all. The audit row is written on
        /// its own connection, so it survives the caller rolling back - an audit trail that
        /// disappeared with the transaction it was recording would be worthless.
        /// </summary>
        [Fact]
        public async Task LogAsync_WritesOnItsOwnConnectionAndSurvivesTheCallersRollback()
        {
            await SeedAsync();

            await using var caller = NewContext();
            await using (var transaction = await caller.Database.BeginTransactionAsync())
            {
                caller.Tasks.Add(TestData.Task(GroupId, LeadId, id: TaskId, title: "doomed"));
                await caller.SaveChangesAsync();

                await NewSut(caller).LogAsync(NewRequest(action: "Created"));

                await transaction.RollbackAsync();
            }

            await using var assert = NewContext();
            Assert.Equal(0, await assert.Tasks.CountAsync());
            Assert.Equal(1, await assert.AuditLogs.CountAsync());
        }

        // =====================================================================
        // GetGroupHistoryAsync
        // =====================================================================

        [Fact]
        public async Task GetGroupHistoryAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();
            await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId);

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetGroupHistoryAsync(GroupId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task GetGroupHistoryAsync_WhenCallerIsNotAMemberOfAnything_ReturnsForbidden()
        {
            await SeedAsync();

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetGroupHistoryAsync(GroupId, OutsiderId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task GetGroupHistoryAsync_ReturnsOnlyThatGroupsRows()
        {
            await SeedAsync();
            await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId, action: "Ours");
            await SeedLogAsync("TaskItem", Guid.NewGuid(), OtherLeadId, OtherGroupId, action: "Theirs");
            await SeedLogAsync("User", LeadId, LeadId, groupId: null, action: "Login");

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetGroupHistoryAsync(GroupId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("Ours", result.Value!.Single().Action);
        }

        [Fact]
        public async Task GetGroupHistoryAsync_ReturnsNewestFirst()
        {
            await SeedAsync();
            var now = DateTime.UtcNow;
            await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId, action: "Oldest", createdAt: now.AddHours(-3));
            await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId, action: "Newest", createdAt: now);
            await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId, action: "Middle", createdAt: now.AddHours(-1));

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetGroupHistoryAsync(GroupId, LeadId);

            Assert.Equal(
                new[] { "Newest", "Middle", "Oldest" },
                result.Value!.Select(l => l.Action));
        }

        [Fact]
        public async Task GetGroupHistoryAsync_Pages()
        {
            await SeedAsync();
            var now = DateTime.UtcNow;
            for (var i = 0; i < 5; i++)
                await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId,
                    action: $"Action {i}", createdAt: now.AddMinutes(-i));

            await using var groupCtx = NewContext();
            var page2 = await NewSut(groupCtx).GetGroupHistoryAsync(GroupId, LeadId, pageNumber: 2, pageSize: 2);

            Assert.Equal(new[] { "Action 2", "Action 3" }, page2.Value!.Select(l => l.Action));
        }

        [Fact]
        public async Task GetGroupHistoryAsync_ProjectsCreatedAtOntoTimestamp()
        {
            await SeedAsync();
            var stamped = DateTime.UtcNow.AddHours(-2);
            await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId, createdAt: stamped);

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetGroupHistoryAsync(GroupId, LeadId);

            var dto = result.Value!.Single();
            Assert.Equal(stamped, dto.Timestamp, TimeSpan.FromSeconds(1));
            Assert.Equal("198.51.100.4", dto.IpAddress);
            Assert.Equal("seeded", dto.UserName);
        }

        // =====================================================================
        // GetEntityHistoryAsync
        // =====================================================================

        [Theory]
        [InlineData("TaskItem")]
        [InlineData("Group")]
        [InlineData("GroupMember")]
        public async Task GetEntityHistoryAsync_ForAKnownEntityType_ChecksMembership(string entityType)
        {
            await SeedAsync();

            Guid entityId;
            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, id: TaskId));
                await db.SaveChangesAsync();

                entityId = entityType switch
                {
                    "TaskItem" => TaskId,
                    "Group" => GroupId,
                    _ => (await db.GroupMembers.FirstAsync(gm => gm.GroupId == GroupId)).Id
                };
            }

            await SeedLogAsync(entityType, entityId, LeadId, GroupId);

            await using var groupCtx = NewContext();
            var sut = NewSut(groupCtx);

            var denied = await sut.GetEntityHistoryAsync(entityType, entityId, OtherLeadId);
            Assert.True(denied.IsFailure);
            Assert.Equal("Forbidden", denied.Error!.Code);

            var allowed = await sut.GetEntityHistoryAsync(entityType, entityId, MemberId);
            Assert.True(allowed.IsSuccess, allowed.Error?.Message);
            Assert.Single(allowed.Value!);
        }

        [Fact]
        public async Task GetEntityHistoryAsync_ReturnsOnlyThatEntitysRows()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, id: TaskId));
                await db.SaveChangesAsync();
            }

            var otherTaskId = Guid.NewGuid();
            await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId, action: "Wanted");
            await SeedLogAsync("TaskItem", otherTaskId, LeadId, GroupId, action: "OtherEntity");
            await SeedLogAsync("Group", TaskId, LeadId, GroupId, action: "OtherType");

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetEntityHistoryAsync("TaskItem", TaskId, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("Wanted", result.Value!.Single().Action);
        }

        /// <summary>
        /// KNOWN HOLE, pinned deliberately. GetEntityGroupIdAsync's switch default-allows any
        /// entity type it does not recognise, so no membership check runs and an outsider reads
        /// the rows. This is finding A in services/Finished/audit-service.md and the only reason
        /// it is not exploitable today is that every AuditController route is [NonAction].
        /// When the winter-2026 rework inverts the default to deny, this test flips to asserting
        /// Forbidden and becomes the regression guard. Do not "fix" the test on its own.
        /// </summary>
        [Fact]
        public async Task GetEntityHistoryAsync_WithAnUnrecognisedEntityType_SkipsTheMembershipCheck_KnownHole()
        {
            await SeedAsync();

            var secretId = Guid.NewGuid();
            await SeedLogAsync("PaymentMethod", secretId, LeadId, GroupId, action: "Rotated");

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetEntityHistoryAsync("PaymentMethod", secretId, OutsiderId);

            Assert.True(result.IsSuccess);
            Assert.Equal("Rotated", result.Value!.Single().Action);
        }

        /// <summary>
        /// The same hole reached a second way: a recognised type whose row no longer exists
        /// resolves to a null group id, which lands on the identical default-allow path.
        /// </summary>
        [Fact]
        public async Task GetEntityHistoryAsync_WhenTheEntityRowIsGone_SkipsTheMembershipCheck_KnownHole()
        {
            await SeedAsync();

            var deletedTaskId = Guid.NewGuid();
            await SeedLogAsync("TaskItem", deletedTaskId, LeadId, GroupId, action: "Deleted");

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetEntityHistoryAsync("TaskItem", deletedTaskId, OutsiderId);

            Assert.True(result.IsSuccess);
            Assert.Single(result.Value!);
        }

        // =====================================================================
        // GetUserHistoryAsync
        // =====================================================================

        [Fact]
        public async Task GetUserHistoryAsync_ExcludesGroupsTheRequesterIsNotIn()
        {
            await SeedAsync();

            await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId, action: "Shared");
            await SeedLogAsync("TaskItem", Guid.NewGuid(), LeadId, OtherGroupId, action: "Hidden");

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetUserHistoryAsync(LeadId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("Shared", result.Value!.Single().Action);
        }

        [Fact]
        public async Task GetUserHistoryAsync_ReturnsOnlyTheRequestedUsersRows()
        {
            await SeedAsync();

            await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId, action: "ByLead");
            await SeedLogAsync("TaskItem", TaskId, MemberId, GroupId, action: "ByMember");

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetUserHistoryAsync(LeadId, MemberId);

            Assert.Equal("ByLead", result.Value!.Single().Action);
        }

        /// <summary>
        /// KNOWN HOLE, pinned deliberately. The `a.GroupId == null` clause means group-less
        /// events - logins, carrying IP address and user agent - come back for ANY target user,
        /// including one the requester shares no group with. This is why GetUserHistoryAsync sits
        /// behind [NonAction]. The winter-2026 rework makes group-less rows self-only, at which
        /// point this test flips to asserting the row is absent.
        /// </summary>
        [Fact]
        public async Task GetUserHistoryAsync_LeaksGrouplessLoginRowsAboutAnyUser_KnownHole()
        {
            await SeedAsync();

            await SeedLogAsync("User", OtherLeadId, OtherLeadId, groupId: null, action: "Login");

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetUserHistoryAsync(OtherLeadId, OutsiderId);

            Assert.True(result.IsSuccess);
            var leaked = result.Value!.Single();
            Assert.Equal("Login", leaked.Action);
            Assert.Equal("198.51.100.4", leaked.IpAddress);
        }

        [Fact]
        public async Task GetUserHistoryAsync_ReturnsNewestFirst()
        {
            await SeedAsync();
            var now = DateTime.UtcNow;
            await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId, action: "Older", createdAt: now.AddHours(-2));
            await SeedLogAsync("TaskItem", TaskId, LeadId, GroupId, action: "Newer", createdAt: now);

            await using var groupCtx = NewContext();
            var result = await NewSut(groupCtx).GetUserHistoryAsync(LeadId, LeadId);

            Assert.Equal(new[] { "Newer", "Older" }, result.Value!.Select(l => l.Action));
        }
    }
}
