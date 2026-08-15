using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class DashboardServiceTests : DbTestBase
    {
        public DashboardServiceTests(PostgresFixture fixture) : base(fixture) { }

        private DashboardService NewSut(IApplicationDbContext context) => new(
            context,
            new GroupService(
                context,
                Mock.Of<IGroupCodeGenerator>(),
                Mock.Of<IPasswordHasher>(),
                NullLogger<GroupService>.Instance),
            NullLogger<DashboardService>.Instance);

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        /// <summary>
        /// Midway between now and midnight, so the due today window is hit without depending on
        /// what time the suite happens to run.
        /// </summary>
        private static DateTime LaterToday()
        {
            var now = DateTime.UtcNow;
            var todayEnd = now.Date.AddDays(1);
            return now.AddTicks((todayEnd - now).Ticks / 2);
        }

        private async Task<Guid> SeedTaskAsync(
            string title,
            Guid? assignedTo = null,
            Guid? groupId = null,
            TaskStatusItem status = TaskStatusItem.InProgress,
            TaskPriority priority = TaskPriority.Medium,
            DateTime? dueDate = null,
            DateTime? completedAt = null,
            DateTime? createdAt = null)
        {
            await using var db = NewContext();

            var task = TestData.Task(
                groupId ?? GroupId, LeadId,
                title: title, status: status, priority: priority,
                assignedTo: assignedTo, dueDate: dueDate);

            task.CompletedAt = completedAt;

            db.Tasks.Add(task);
            await db.SaveChangesAsync();

            if (createdAt.HasValue)
                await db.BackdateAsync<TaskItem>(task.Id, createdAt.Value);

            return task.Id;
        }

        private async Task SeedAuditAsync(Guid groupId, string action, DateTime createdAt)
        {
            await using var db = NewContext();

            var log = new AuditLog
            {
                EntityType = "Task",
                EntityId = Guid.NewGuid(),
                Action = action,
                UserId = LeadId,
                UserName = "lead",
                UserEmail = "lead@example.com",
                GroupId = groupId,
                IpAddress = "1.1.1.1",
                UserAgent = "test"
            };

            db.AuditLogs.Add(log);
            await db.SaveChangesAsync();

            await db.BackdateAsync<AuditLog>(log.Id, createdAt);
        }

        [Fact]
        public async Task GetPersonalDashboardAsync_SortsOpenTasksIntoOverdueTodayAndThisWeek()
        {
            await SeedAsync();

            await SeedTaskAsync("Overdue", MemberId, dueDate: DateTime.UtcNow.AddHours(-1));
            await SeedTaskAsync("Today", MemberId, dueDate: LaterToday());
            await SeedTaskAsync("This week", MemberId, dueDate: DateTime.UtcNow.Date.AddDays(2));

            await using var act = NewContext();
            var result = await NewSut(act).GetPersonalDashboardAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            var dashboard = result.Value!;

            Assert.Equal("Overdue", Assert.Single(dashboard.OverdueTasks).Title);
            Assert.Equal("Today", Assert.Single(dashboard.DueToday).Title);
            Assert.Equal("This week", Assert.Single(dashboard.DueThisWeek).Title);
        }

        /// <summary>
        /// The query pulls one bounded slice rather than every task ever assigned, so anything
        /// due beyond the week window is deliberately absent from all three buckets.
        /// </summary>
        [Fact]
        public async Task GetPersonalDashboardAsync_IgnoresTasksDueBeyondTheWeekWindow()
        {
            await SeedAsync();

            await SeedTaskAsync("Next month", MemberId, dueDate: DateTime.UtcNow.AddDays(30));

            await using var act = NewContext();
            var dashboard = (await NewSut(act).GetPersonalDashboardAsync(MemberId)).Value!;

            Assert.Empty(dashboard.OverdueTasks);
            Assert.Empty(dashboard.DueToday);
            Assert.Empty(dashboard.DueThisWeek);
        }

        [Fact]
        public async Task GetPersonalDashboardAsync_KeepsCompletedTasksOutOfTheDueBuckets()
        {
            await SeedAsync();

            await SeedTaskAsync("Done but late", MemberId,
                status: TaskStatusItem.Completed,
                dueDate: DateTime.UtcNow.AddDays(-2),
                completedAt: DateTime.UtcNow.AddDays(-1));

            await using var act = NewContext();
            var dashboard = (await NewSut(act).GetPersonalDashboardAsync(MemberId)).Value!;

            Assert.Empty(dashboard.OverdueTasks);
            Assert.Single(dashboard.RecentlyCompleted);
        }

        [Fact]
        public async Task GetPersonalDashboardAsync_ShowsOnlyTheCallersOwnTasks()
        {
            await SeedAsync();

            await SeedTaskAsync("Mine", MemberId, dueDate: DateTime.UtcNow.AddHours(-1));
            await SeedTaskAsync("Somebody elses", LeadId, dueDate: DateTime.UtcNow.AddHours(-1));

            await using var act = NewContext();
            var dashboard = (await NewSut(act).GetPersonalDashboardAsync(MemberId)).Value!;

            Assert.Equal("Mine", Assert.Single(dashboard.OverdueTasks).Title);
        }

        /// <summary>
        /// Recently completed is a seven day window while the trend reaches back thirty, so a
        /// completion older than a week counts towards the chart and not towards the list.
        /// </summary>
        [Fact]
        public async Task GetPersonalDashboardAsync_GetPersonalDashboardAsync_LimitsRecentlyCompletedToSevenDaysWhileTheTrendKeepsThirty()
        {
            await SeedAsync();

            await SeedTaskAsync("Finished yesterday", MemberId,
                status: TaskStatusItem.Completed, completedAt: DateTime.UtcNow.AddDays(-1));
            await SeedTaskAsync("Finished a fortnight ago", MemberId,
                status: TaskStatusItem.Completed, completedAt: DateTime.UtcNow.AddDays(-14));

            await using var act = NewContext();
            var dashboard = (await NewSut(act).GetPersonalDashboardAsync(MemberId)).Value!;

            Assert.Equal("Finished yesterday", Assert.Single(dashboard.RecentlyCompleted).Title);
            Assert.Equal(2, dashboard.CompletionTrend.Sum(p => p.CompletedCount));

        }

        [Fact]
        public async Task GetPersonalDashboardAsync_ReturnsThirtyTrendPointsWithEmptyDaysFilledIn()
        {
            await SeedAsync();

            await SeedTaskAsync("Finished", MemberId,
                status: TaskStatusItem.Completed, completedAt: DateTime.UtcNow.AddDays(-3));

            await using var act = NewContext();
            var dashboard = (await NewSut(act).GetPersonalDashboardAsync(MemberId)).Value!;

            Assert.Equal(30, dashboard.CompletionTrend.Count);
            Assert.Equal(29, dashboard.CompletionTrend.Count(p => p.CompletedCount == 0));

            var expectedDay = DateTime.UtcNow.Date.AddDays(-3);
            Assert.Equal(1, dashboard.CompletionTrend.Single(p => p.Date == expectedDay).CompletedCount);
        }

        [Fact]
        public async Task GetPersonalDashboardAsync_RunsTheTrendUpToAndIncludingToday()
        {
            await SeedAsync();

            await using var act = NewContext();
            var dashboard = (await NewSut(act).GetPersonalDashboardAsync(MemberId)).Value!;

            Assert.Equal(DateTime.UtcNow.Date.AddDays(-29), dashboard.CompletionTrend.First().Date);
            Assert.Equal(DateTime.UtcNow.Date, dashboard.CompletionTrend.Last().Date);
        }

        /// <summary>
        /// The totals come from their own grouped count over every assigned task, not from the
        /// bounded slice, so a task outside the due window still counts towards them.
        /// </summary>
        [Fact]
        public async Task GetPersonalDashboardAsync_CountsEveryAssignedTaskAndNotOnlyTheBoundedSlice()
        {
            await SeedAsync();

            await SeedTaskAsync("Far future", MemberId, dueDate: DateTime.UtcNow.AddDays(90));
            await SeedTaskAsync("No due date", MemberId);
            await SeedTaskAsync("Ancient completion", MemberId,
                status: TaskStatusItem.Completed, completedAt: DateTime.UtcNow.AddDays(-200));

            await using var act = NewContext();
            var dashboard = (await NewSut(act).GetPersonalDashboardAsync(MemberId)).Value!;

            Assert.Equal(2, dashboard.TotalAssignedTasks);
            Assert.Equal(1, dashboard.TotalCompletedTasks);
            Assert.Empty(dashboard.OverdueTasks);
            Assert.Empty(dashboard.DueThisWeek);
        }

        /// <summary>
        /// The counts query groups by a constant, so a user with no tasks at all gets no row back
        /// rather than a row of zeroes. The null coalesce is what turns that into zero.
        /// </summary>
        [Fact]
        public async Task GetPersonalDashboardAsync_ReportsZeroesForSomebodyWithNoTasks()
        {
            await SeedAsync();

            await using var act = NewContext();
            var dashboard = (await NewSut(act).GetPersonalDashboardAsync(MemberId)).Value!;

            Assert.Equal(0, dashboard.TotalAssignedTasks);
            Assert.Equal(0, dashboard.TotalCompletedTasks);
            Assert.Equal(1, dashboard.GroupCount);
            Assert.Equal(30, dashboard.CompletionTrend.Count);
            Assert.All(dashboard.CompletionTrend, p => Assert.Equal(0, p.CompletedCount));
        }

        [Fact]
        public async Task GetPersonalDashboardAsync_CountsTheGroupsTheCallerBelongsTo()
        {
            await SeedAsync();

            await using var act = NewContext();

            Assert.Equal(1, (await NewSut(act).GetPersonalDashboardAsync(MemberId)).Value!.GroupCount);
            Assert.Equal(0, (await NewSut(act).GetPersonalDashboardAsync(OutsiderId)).Value!.GroupCount);
        }

        [Fact]
        public async Task GetPersonalDashboardAsync_ShowsActivityFromTheCallersGroupsNewestFirst()
        {
            await SeedAsync();

            var now = DateTime.UtcNow;
            await SeedAuditAsync(GroupId, "Oldest", now.AddHours(-3));
            await SeedAuditAsync(GroupId, "Newest", now);
            await SeedAuditAsync(GroupId, "Middle", now.AddHours(-1));
            await SeedAuditAsync(OtherGroupId, "Another group", now);

            await using var act = NewContext();
            var dashboard = (await NewSut(act).GetPersonalDashboardAsync(MemberId)).Value!;

            Assert.Equal(
                new[] { "Newest", "Middle", "Oldest" },
                dashboard.RecentActivity.Select(a => a.Action));
        }

        [Fact]
        public async Task GetPersonalDashboardAsync_CapsRecentActivityAtFifteenEntries()
        {
            await SeedAsync();

            var now = DateTime.UtcNow;
            for (var i = 0; i < 20; i++)
                await SeedAuditAsync(GroupId, $"Action {i}", now.AddMinutes(-i));

            await using var act = NewContext();
            var dashboard = (await NewSut(act).GetPersonalDashboardAsync(MemberId)).Value!;

            Assert.Equal(15, dashboard.RecentActivity.Count);
            Assert.Equal("Action 0", dashboard.RecentActivity.First().Action);
        }

        [Fact]
        public async Task GetPersonalDashboardAsync_SkipsTheActivityQueryEntirelyForSomebodyInNoGroups()
        {
            await SeedAsync();
            await SeedAuditAsync(GroupId, "Something", DateTime.UtcNow);

            await using var act = NewContext();
            var dashboard = (await NewSut(act).GetPersonalDashboardAsync(OutsiderId)).Value!;

            Assert.Empty(dashboard.RecentActivity);
        }

        [Fact]
        public async Task GetFieldDataAsync_ReturnsOneTreePerGroupTheCallerBelongsTo()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetFieldDataAsync(MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            var tree = Assert.Single(result.Value!);
            Assert.Equal(GroupId, tree.GroupId);
            Assert.Equal("Dev Team", tree.GroupName);
            Assert.Equal(2, tree.MemberCount);
        }

        [Fact]
        public async Task GetFieldDataAsync_IsEmptyForSomebodyInNoGroups()
        {
            await SeedAsync();

            await using var act = NewContext();
            Assert.Empty((await NewSut(act).GetFieldDataAsync(OutsiderId)).Value!);
        }

        [Theory]
        [InlineData(0, 0, 0.0, TreeStage.EmptySoil)]
        [InlineData(4, 1, 25.0, TreeStage.Sprout)]
        [InlineData(4, 2, 50.0, TreeStage.Sapling)]
        [InlineData(3, 1, 33.3, TreeStage.Sprout)]
        [InlineData(2, 2, 100.0, TreeStage.FloweringTree)]
        public async Task GetFieldDataAsync_DerivesTheStageFromTheCompletionPercentage(
            int total, int completed, double expectedPercentage, TreeStage expectedStage)
        {
            await SeedAsync();

            for (var i = 0; i < total; i++)
            {
                var isCompleted = i < completed;
                await SeedTaskAsync($"Task {i}", LeadId,
                    status: isCompleted ? TaskStatusItem.Completed : TaskStatusItem.InProgress,
                    completedAt: isCompleted ? DateTime.UtcNow : null);
            }

            await using var act = NewContext();
            var tree = Assert.Single((await NewSut(act).GetFieldDataAsync(MemberId)).Value!);

            Assert.Equal(expectedPercentage, tree.CompletionPercentage);
            Assert.Equal(expectedStage, tree.CurrentTreeStage);
            Assert.Equal(total, tree.TotalTasks);
            Assert.Equal(completed, tree.CompletedTasks);
        }

        /// <summary>
        /// The two aggregates are separate grouped queries joined back up by dictionary lookup,
        /// so a group with no tasks at all has no row in the task stats and has to fall back to
        /// zero rather than disappearing from the field.
        /// </summary>
        [Fact]
        public async Task GetFieldDataAsync_StillShowsAGroupThatHasNoTasksYet()
        {
            await SeedAsync();

            await using var act = NewContext();
            var tree = Assert.Single((await NewSut(act).GetFieldDataAsync(MemberId)).Value!);

            Assert.Equal(0, tree.TotalTasks);
            Assert.Equal(0.0, tree.CompletionPercentage);
            Assert.Equal(TreeStage.EmptySoil, tree.CurrentTreeStage);
            Assert.Equal(2, tree.MemberCount);
        }

        [Fact]
        public async Task GetFieldDataAsync_DoesNotCountAnotherGroupsTasks()
        {
            await SeedAsync();

            await SeedTaskAsync("Ours", LeadId, status: TaskStatusItem.Completed, completedAt: DateTime.UtcNow);
            await SeedTaskAsync("Theirs", OtherLeadId, groupId: OtherGroupId);

            await using var act = NewContext();
            var tree = Assert.Single((await NewSut(act).GetFieldDataAsync(MemberId)).Value!);

            Assert.Equal(1, tree.TotalTasks);
            Assert.Equal(100.0, tree.CompletionPercentage);
        }

        [Fact]
        public async Task GetGroupStatisticsAsync_RefusesANonMember()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupStatisticsAsync(GroupId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        /// <summary>
        /// Membership is checked before the group is looked up, so an id that does not exist
        /// answers Forbidden rather than NotFound and tells a stranger nothing.
        /// </summary>
        [Fact]
        public async Task GetGroupStatisticsAsync_AnswersForbiddenForAGroupThatDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupStatisticsAsync(Guid.NewGuid(), MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task GetGroupStatisticsAsync_CountsEveryStatusBucket()
        {
            await SeedAsync();

            await SeedTaskAsync("A", status: TaskStatusItem.NotStarted);
            await SeedTaskAsync("B", status: TaskStatusItem.InProgress);
            await SeedTaskAsync("C", status: TaskStatusItem.InProgress);
            await SeedTaskAsync("D", status: TaskStatusItem.UnderReview);
            await SeedTaskAsync("E", status: TaskStatusItem.Completed, completedAt: DateTime.UtcNow);

            await using var act = NewContext();
            var stats = (await NewSut(act).GetGroupStatisticsAsync(GroupId, MemberId)).Value!;

            Assert.Equal(5, stats.TotalTasks);
            Assert.Equal(1, stats.NotStartedTasks);
            Assert.Equal(2, stats.InProgressTasks);
            Assert.Equal(1, stats.UnderReviewTasks);
            Assert.Equal(1, stats.CompletedTasks);
            Assert.Equal(20.0, stats.CompletionPercentage);
            Assert.Equal(2, stats.MemberCount);
        }

        [Fact]
        public async Task GetGroupStatisticsAsync_ExcludesCompletedTasksFromTheOverdueCount()
        {
            await SeedAsync();

            var past = DateTime.UtcNow.AddDays(-2);
            await SeedTaskAsync("Still open", dueDate: past);
            await SeedTaskAsync("Finished late", status: TaskStatusItem.Completed,
                dueDate: past, completedAt: DateTime.UtcNow.AddDays(-1));
            await SeedTaskAsync("Not due yet", dueDate: DateTime.UtcNow.AddDays(5));
            await SeedTaskAsync("No due date");

            await using var act = NewContext();
            var stats = (await NewSut(act).GetGroupStatisticsAsync(GroupId, MemberId)).Value!;

            Assert.Equal(1, stats.OverdueTasks);
        }

        [Fact]
        public async Task GetGroupStatisticsAsync_GroupsTasksByStatusAndPriorityWithTheirColours()
        {
            await SeedAsync();

            await SeedTaskAsync("A", status: TaskStatusItem.InProgress, priority: TaskPriority.High);
            await SeedTaskAsync("B", status: TaskStatusItem.InProgress, priority: TaskPriority.Low);
            await SeedTaskAsync("C", status: TaskStatusItem.NotStarted, priority: TaskPriority.High);

            await using var act = NewContext();
            var stats = (await NewSut(act).GetGroupStatisticsAsync(GroupId, MemberId)).Value!;

            var inProgress = stats.TasksByStatus.Single(s => s.StatusName == "In Progress");
            Assert.Equal(2, inProgress.Count);
            Assert.Equal("#0dcaf0", inProgress.Color);

            var high = stats.TasksByPriority.Single(p => p.PriorityName == "High");
            Assert.Equal(2, high.Count);
            Assert.Equal("#ffc107", high.Color);
        }

        /// <summary>
        /// The breakdowns only list buckets that actually have tasks, because they are built by
        /// grouping the rows rather than by walking the lookup table.
        /// </summary>
        [Fact]
        public async Task GetGroupStatisticsAsync_OmitsStatusesNothingIsIn()
        {
            await SeedAsync();

            await SeedTaskAsync("A", status: TaskStatusItem.InProgress);

            await using var act = NewContext();
            var stats = (await NewSut(act).GetGroupStatisticsAsync(GroupId, MemberId)).Value!;

            Assert.Equal("In Progress", Assert.Single(stats.TasksByStatus).StatusName);
        }

        [Fact]
        public async Task GetGroupStatisticsAsync_BuildsMemberWorkloadOrderedByOpenTasks()
        {
            await SeedAsync();

            await SeedTaskAsync("Lead one", LeadId);
            await SeedTaskAsync("Member one", MemberId);
            await SeedTaskAsync("Member two", MemberId);
            await SeedTaskAsync("Member overdue", MemberId, dueDate: DateTime.UtcNow.AddDays(-1));
            await SeedTaskAsync("Member done", MemberId,
                status: TaskStatusItem.Completed, completedAt: DateTime.UtcNow);
            await SeedTaskAsync("Nobody's");

            await using var act = NewContext();
            var stats = (await NewSut(act).GetGroupStatisticsAsync(GroupId, MemberId)).Value!;

            Assert.Equal(2, stats.MemberWorkload.Count);

            var member = stats.MemberWorkload.First();
            Assert.Equal("member", member.UserName);
            Assert.Equal(3, member.AssignedCount);
            Assert.Equal(1, member.CompletedCount);
            Assert.Equal(1, member.OverdueCount);

            Assert.Equal("lead", stats.MemberWorkload.Last().UserName);
        }

        /// <summary>
        /// The date subtraction does not translate to SQL, so the two columns are fetched and the
        /// average is computed in memory. Nothing completed means null rather than zero, because
        /// zero days would read as instant completion.
        /// </summary>
        [Fact]
        public async Task GetGroupStatisticsAsync_ReportsNoAverageCompletionTimeWhenNothingIsFinished()
        {
            await SeedAsync();

            await SeedTaskAsync("Still going");

            await using var act = NewContext();
            var stats = (await NewSut(act).GetGroupStatisticsAsync(GroupId, MemberId)).Value!;

            Assert.Null(stats.AverageCompletionDays);
        }

        [Fact]
        public async Task GetGroupStatisticsAsync_AveragesTheDaysBetweenCreationAndCompletion()
        {
            await SeedAsync();

            var now = DateTime.UtcNow;

            await SeedTaskAsync("Two days", status: TaskStatusItem.Completed,
                createdAt: now.AddDays(-2), completedAt: now);
            await SeedTaskAsync("Four days", status: TaskStatusItem.Completed,
                createdAt: now.AddDays(-4), completedAt: now);

            await using var act = NewContext();
            var stats = (await NewSut(act).GetGroupStatisticsAsync(GroupId, MemberId)).Value!;

            Assert.NotNull(stats.AverageCompletionDays);
            Assert.Equal(3.0, stats.AverageCompletionDays!.Value, precision: 1);
        }

        [Fact]
        public async Task GetGroupStatisticsAsync_ReturnsThirtyTrendPointsEndingToday()
        {
            await SeedAsync();

            await SeedTaskAsync("Finished", status: TaskStatusItem.Completed,
                completedAt: DateTime.UtcNow.AddDays(-5));

            await using var act = NewContext();
            var stats = (await NewSut(act).GetGroupStatisticsAsync(GroupId, MemberId)).Value!;

            Assert.Equal(30, stats.CompletionTrend.Count);
            Assert.Equal(DateTime.UtcNow.Date, stats.CompletionTrend.Last().Date);
            Assert.Equal(1, stats.CompletionTrend.Sum(p => p.CompletedCount));
        }

        [Fact]
        public async Task GetGroupStatisticsAsync_LeavesCompletionsOlderThanTheWindowOutOfTheTrend()
        {
            await SeedAsync();

            await SeedTaskAsync("Ancient", status: TaskStatusItem.Completed,
                completedAt: DateTime.UtcNow.AddDays(-90));

            await using var act = NewContext();
            var stats = (await NewSut(act).GetGroupStatisticsAsync(GroupId, MemberId)).Value!;

            Assert.Equal(0, stats.CompletionTrend.Sum(p => p.CompletedCount));
            Assert.Equal(1, stats.CompletedTasks);
        }

        [Fact]
        public async Task GetGroupStatisticsAsync_CountsOnlyThatGroupsTasks()
        {
            await SeedAsync();

            await SeedTaskAsync("Ours");
            await SeedTaskAsync("Theirs", groupId: OtherGroupId);

            await using var act = NewContext();
            var stats = (await NewSut(act).GetGroupStatisticsAsync(GroupId, MemberId)).Value!;

            Assert.Equal(1, stats.TotalTasks);
        }

        /// <summary>
        /// This one deliberately has no authorization. It is only reachable from
        /// TreeProgressBroadcaster which emits into the membership gated group room, and the
        /// summary on it says never to expose it through a controller without adding a check.
        /// The test records that as current behaviour rather than approving of it.
        /// </summary>
        [Fact]
        public async Task GetGroupTreeProgressAsync_HasNoMembershipCheckOfItsOwn()
        {
            await SeedAsync();
            await SeedTaskAsync("Done", status: TaskStatusItem.Completed, completedAt: DateTime.UtcNow);

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupTreeProgressAsync(GroupId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(100.0, result.Value!.CompletionPercentage);
            Assert.Equal(TreeStage.FloweringTree, result.Value.CurrentTreeStage);
        }

        [Fact]
        public async Task GetGroupTreeProgressAsync_ReturnsNotFoundForAGroupThatDoesNotExist()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupTreeProgressAsync(Guid.NewGuid());

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task GetGroupTreeProgressAsync_ReportsBareSoilForAGroupWithNoTasks()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupTreeProgressAsync(GroupId);

            Assert.Equal(0.0, result.Value!.CompletionPercentage);
            Assert.Equal(TreeStage.EmptySoil, result.Value.CurrentTreeStage);
            Assert.Equal(0, result.Value.TotalTasks);
            Assert.Equal(2, result.Value.MemberCount);
        }
    }
}