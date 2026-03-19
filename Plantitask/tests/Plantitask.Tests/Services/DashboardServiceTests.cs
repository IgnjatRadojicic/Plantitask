using Microsoft.Extensions.Logging;
using Moq;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestDataBuilder;

namespace Plantitask.Tests.Services;

public class DashboardServiceTests
{
    private readonly Mock<IApplicationDbContext> _mockContext;
    private readonly Mock<ILogger<DashboardService>> _mockLogger;
    private readonly DashboardService _sut;

    public DashboardServiceTests()
    {
        _mockContext = new Mock<IApplicationDbContext>();
        _mockLogger = new Mock<ILogger<DashboardService>>();
        _sut = new DashboardService(_mockContext.Object, _mockLogger.Object);
    }


    [Theory]
    [InlineData(0, 0, TreeStage.EmptySoil)]
    [InlineData(1, 10, TreeStage.Seed)]
    [InlineData(2, 10, TreeStage.Sprout)]
    [InlineData(4, 10, TreeStage.Sapling)]
    [InlineData(7, 10, TreeStage.YoungTree)]
    [InlineData(9, 10, TreeStage.FullTree)]
    [InlineData(10, 10, TreeStage.FloweringTree)]
    public async Task GetFieldDataAsync_CorrectTreeStagePerCompletionPercentage(
        int completedCount, int totalCount, TreeStage expectedStage)
    {
        var group = CreateGroup(GroupId1, "Test Group");
        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        };
        memberships[0].Group = group;
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);

        var tasks = new List<TaskItem>();
        for (int i = 0; i < totalCount; i++)
        {
            tasks.Add(CreateTask(
                groupId: GroupId1,
                statusId: i < completedCount ? (int)TaskStatusItem.Completed : (int)TaskStatusItem.NotStarted,
                completedAt: i < completedCount ? DateTime.UtcNow.AddDays(-1) : null));
        }
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(tasks).Object);

        var result = await _sut.GetFieldDataAsync(UserId1);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(expectedStage, result.Value[0].CurrentTreeStage);
        Assert.Equal(completedCount, result.Value[0].CompletedTasks);
        Assert.Equal(totalCount, result.Value[0].TotalTasks);
    }

    [Fact]
    public async Task GetFieldDataAsync_GroupWithNoTasks_ReturnsEmptySoil()
    {
        var group = CreateGroup(GroupId1);
        var memberships = new List<GroupMember> { CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member) };
        memberships[0].Group = group;

        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem>()).Object);

        var result = await _sut.GetFieldDataAsync(UserId1);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(TreeStage.EmptySoil, result.Value[0].CurrentTreeStage);
        Assert.Equal(0, result.Value[0].CompletionPercentage);
    }

    [Fact]
    public async Task GetFieldDataAsync_MultipleGroups_ReturnsTreePerGroup()
    {
        var group1 = CreateGroup(GroupId1, "Group A");
        var group2 = CreateGroup(GroupId2, "Group B");

        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member),
            CreateMembership(UserId1, GroupId2, roleId: RoleIds.TeamLead),
        };
        memberships[0].Group = group1;
        memberships[1].Group = group2;
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);

        var tasks = new List<TaskItem>
        {
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.Completed, completedAt: DateTime.UtcNow),
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.NotStarted),
            CreateTask(groupId: GroupId2, statusId: (int)TaskStatusItem.Completed, completedAt: DateTime.UtcNow),
        };
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(tasks).Object);

        var result = await _sut.GetFieldDataAsync(UserId1);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);

        var groupA = result.Value.First(t => t.GroupName == "Group A");
        Assert.Equal(50.0, groupA.CompletionPercentage);
        Assert.Equal(TreeStage.Sapling, groupA.CurrentTreeStage);

        var groupB = result.Value.First(t => t.GroupName == "Group B");
        Assert.Equal(100.0, groupB.CompletionPercentage);
        Assert.Equal(TreeStage.FloweringTree, groupB.CurrentTreeStage);
    }


    [Fact]
    public async Task GetPersonalDashboardAsync_CategorizesTasksByDueDate()
    {
        var now = DateTime.UtcNow;

        var tasks = new List<TaskItem>
        {
            CreateTask(assignedToId: UserId1, groupId: GroupId1, statusId: (int)TaskStatusItem.InProgress,
                dueDate: now.AddDays(-2), title: "Overdue Task"),
            CreateTask(assignedToId: UserId1, groupId: GroupId1, statusId: (int)TaskStatusItem.InProgress,
                dueDate: now.AddHours(3), title: "Due Today Task"),
            CreateTask(assignedToId: UserId1, groupId: GroupId1, statusId: (int)TaskStatusItem.NotStarted,
                dueDate: now.AddDays(3), title: "Due This Week"),
            CreateTask(assignedToId: UserId1, groupId: GroupId1, statusId: (int)TaskStatusItem.Completed,
                completedAt: now.AddDays(-1), title: "Completed Recently"),
        };

        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        }).Object);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(tasks).Object);
        _mockContext.Setup(c => c.AuditLogs).Returns(MockDbSetFactory.Create(new List<AuditLog>()).Object);

        var result = await _sut.GetPersonalDashboardAsync(UserId1);

        Assert.True(result.IsSuccess);
        var dashboard = result.Value!;
        Assert.Single(dashboard.OverdueTasks);
        Assert.Equal("Overdue Task", dashboard.OverdueTasks[0].Title);
        Assert.Single(dashboard.DueToday);
        Assert.Equal("Due Today Task", dashboard.DueToday[0].Title);
        Assert.Single(dashboard.DueThisWeek);
        Assert.Equal("Due This Week", dashboard.DueThisWeek[0].Title);
        Assert.Single(dashboard.RecentlyCompleted);
        Assert.Equal(3, dashboard.TotalAssignedTasks);
        Assert.Equal(1, dashboard.TotalCompletedTasks);
        Assert.Equal(1, dashboard.GroupCount);
    }

    [Fact]
    public async Task GetPersonalDashboardAsync_CompletedTasksNotInOverdue()
    {
        var completedOverdue = CreateTask(assignedToId: UserId1, groupId: GroupId1,
            statusId: (int)TaskStatusItem.Completed,
            dueDate: DateTime.UtcNow.AddDays(-5),
            completedAt: DateTime.UtcNow.AddDays(-4));

        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        }).Object);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { completedOverdue }).Object);
        _mockContext.Setup(c => c.AuditLogs).Returns(MockDbSetFactory.Create(new List<AuditLog>()).Object);

        var result = await _sut.GetPersonalDashboardAsync(UserId1);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.OverdueTasks);
        Assert.Single(result.Value.RecentlyCompleted);
    }

    [Fact]
    public async Task GetPersonalDashboardAsync_RecentActivity_FiltersToUserGroups()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        }).Object);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem>()).Object);

        var auditLogs = new List<AuditLog>
        {
            CreateAuditLog(groupId: GroupId1, action: "Created"),
            CreateAuditLog(groupId: GroupId2, action: "Deleted"),
            CreateAuditLog(groupId: null, action: "Login"),
        };
        _mockContext.Setup(c => c.AuditLogs).Returns(MockDbSetFactory.Create(auditLogs).Object);

        var result = await _sut.GetPersonalDashboardAsync(UserId1);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.RecentActivity);
        Assert.Equal("Created", result.Value.RecentActivity[0].Action);
    }

    [Fact]
    public async Task GetPersonalDashboardAsync_NoTasks_ReturnsEmptyDashboard()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>()).Object);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem>()).Object);
        _mockContext.Setup(c => c.AuditLogs).Returns(MockDbSetFactory.Create(new List<AuditLog>()).Object);

        var result = await _sut.GetPersonalDashboardAsync(UserId1);

        Assert.True(result.IsSuccess);
        var dashboard = result.Value!;
        Assert.Empty(dashboard.OverdueTasks);
        Assert.Empty(dashboard.DueToday);
        Assert.Empty(dashboard.DueThisWeek);
        Assert.Equal(0, dashboard.TotalAssignedTasks);
        Assert.Equal(0, dashboard.GroupCount);
    }


    [Fact]
    public async Task GetGroupStatisticsAsync_NonMember_ReturnsForbidden()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>()).Object);

        var result = await _sut.GetGroupStatisticsAsync(GroupId1, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
    }

    [Fact]
    public async Task GetGroupStatisticsAsync_GroupNotFound_ReturnsNotFound()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        }).Object);
        _mockContext.Setup(c => c.Groups).Returns(MockDbSetFactory.Create(new List<Group>()).Object);
        _mockContext.Setup(c => c.Groups.FindAsync(GroupId1)).ReturnsAsync((Group?)null);

        var result = await _sut.GetGroupStatisticsAsync(GroupId1, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task GetGroupStatisticsAsync_CalculatesAllStatusCounts()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        }).Object);

        var group = CreateGroup(GroupId1, "Stats Group");
        _mockContext.Setup(c => c.Groups).Returns(MockDbSetFactory.Create(new List<Group> { group }).Object);
        _mockContext.Setup(c => c.Groups.FindAsync(GroupId1)).ReturnsAsync(group);

        var tasks = new List<TaskItem>
        {
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.NotStarted, assignedToId: UserId1),
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.InProgress, assignedToId: UserId1),
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.UnderReview, assignedToId: UserId2),
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.Completed, completedAt: DateTime.UtcNow.AddDays(-1), assignedToId: UserId2),
        };
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(tasks).Object);

        var result = await _sut.GetGroupStatisticsAsync(GroupId1, UserId1);

        Assert.True(result.IsSuccess);
        var stats = result.Value!;
        Assert.Equal(4, stats.TotalTasks);
        Assert.Equal(1, stats.CompletedTasks);
        Assert.Equal(1, stats.InProgressTasks);
        Assert.Equal(1, stats.NotStartedTasks);
        Assert.Equal(1, stats.UnderReviewTasks);
        Assert.Equal(25.0, stats.CompletionPercentage);
    }

    [Fact]
    public async Task GetGroupStatisticsAsync_OverdueCount_ExcludesCompletedTasks()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        }).Object);

        var group = CreateGroup(GroupId1);
        _mockContext.Setup(c => c.Groups).Returns(MockDbSetFactory.Create(new List<Group> { group }).Object);
        _mockContext.Setup(c => c.Groups.FindAsync(GroupId1)).ReturnsAsync(group);

        var now = DateTime.UtcNow;
        var tasks = new List<TaskItem>
        {
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.InProgress,
                dueDate: now.AddDays(-3), assignedToId: UserId1),
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.Completed,
                dueDate: now.AddDays(-3), completedAt: now.AddDays(-2), assignedToId: UserId1),
        };
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(tasks).Object);

        var result = await _sut.GetGroupStatisticsAsync(GroupId1, UserId1);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.OverdueTasks);
    }

    [Fact]
    public async Task GetGroupStatisticsAsync_AverageCompletionDays_CalculatesCorrectly()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        }).Object);

        var group = CreateGroup(GroupId1);
        _mockContext.Setup(c => c.Groups).Returns(MockDbSetFactory.Create(new List<Group> { group }).Object);
        _mockContext.Setup(c => c.Groups.FindAsync(GroupId1)).ReturnsAsync(group);

        var now = DateTime.UtcNow;
        var tasks = new List<TaskItem>
        {
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.Completed,
                completedAt: now.AddDays(-1), assignedToId: UserId1),
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.Completed,
                completedAt: now, assignedToId: UserId1),
        };
        tasks[0].CreatedAt = now.AddDays(-3);
        tasks[1].CreatedAt = now.AddDays(-4);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(tasks).Object);

        var result = await _sut.GetGroupStatisticsAsync(GroupId1, UserId1);

        Assert.True(result.IsSuccess);
        Assert.Equal(3.0, result.Value!.AverageCompletionDays);
    }
}
