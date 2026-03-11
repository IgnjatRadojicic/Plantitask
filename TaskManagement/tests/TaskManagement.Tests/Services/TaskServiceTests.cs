using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using TaskManagement.Core.Constants;
using TaskManagement.Core.DTO.Kanban;
using TaskManagement.Core.DTO.Tasks;
using TaskManagement.Core.Entities;
using TaskManagement.Core.Entities.Lookups;
using TaskManagement.Core.Enums;
using TaskManagement.Core.Interfaces;
using TaskManagement.Infrastructure.Services;
using TaskManagement.Tests.Helpers;
using static TaskManagement.Tests.Helpers.TestDataBuilder;

namespace TaskManagement.Tests.Services;

public class TaskServiceTests
{
    private readonly Mock<IApplicationDbContext> _mockContext;
    private readonly Mock<ILogger<TaskService>> _mockLogger;
    private readonly Mock<IBackgroundJobService> _mockBackgroundJob;
    private readonly TaskService _sut;

    public TaskServiceTests()
    {
        _mockContext = new Mock<IApplicationDbContext>();
        _mockLogger = new Mock<ILogger<TaskService>>();
        _mockBackgroundJob = new Mock<IBackgroundJobService>();

        _sut = new TaskService(
            _mockContext.Object,
            _mockLogger.Object,
            _mockBackgroundJob.Object);
    }


    [Fact]
    public async Task CreateTaskAsync_WhenUserIsNotGroupMember_ThrowsUnauthorized()
    {
        var memberships = new List<GroupMember>();
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);

        var dto = new CreateTaskDto { Title = "New Task", PriorityId = (int)TaskPriority.Medium };

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.CreateTaskAsync(GroupId1, dto, UserId1));

        Assert.Contains("member of this group", ex.Message);
    }

    [Fact]
    public async Task CreateTaskAsync_WhenMemberPermissionBelowTeamLead_ThrowsUnauthorized()
    {
        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        };
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);

        var dto = new CreateTaskDto { Title = "New Task", PriorityId = (int)TaskPriority.Medium };

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.CreateTaskAsync(GroupId1, dto, UserId1));

        Assert.Contains("Team Leads", ex.Message);
    }

    [Fact]
    public async Task CreateTaskAsync_WhenAssigneeNotGroupMember_ThrowsInvalidOperation()
    {
        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.TeamLead)
        };
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);

        var priorities = CreatePriorities();
        _mockContext.Setup(c => c.TaskPriorities).Returns(MockDbSetFactory.Create(priorities).Object);

        var dto = new CreateTaskDto
        {
            Title = "New Task",
            PriorityId = (int)TaskPriority.Medium,
            AssignedToUserId = UserId2
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.CreateTaskAsync(GroupId1, dto, UserId1));
    }

    [Fact]
    public async Task CreateTaskAsync_WithValidTeamLead_SchedulesDueSoonNotification()
    {
        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.TeamLead),
            CreateMembership(UserId2, GroupId1, roleId: RoleIds.Member)
        };
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);

        _mockContext.Setup(c => c.TaskPriorities).Returns(MockDbSetFactory.Create(CreatePriorities()).Object);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem>()).Object);
        _mockContext.Setup(c => c.TaskStatuses).Returns(MockDbSetFactory.Create(CreateStatuses()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var dueDate = DateTime.UtcNow.AddDays(3);
        var dto = new CreateTaskDto
        {
            Title = "New Task",
            Description = "Description",
            PriorityId = (int)TaskPriority.Medium,
            AssignedToUserId = UserId2,
            DueDate = dueDate
        };

        try
        {
            await _sut.CreateTaskAsync(GroupId1, dto, UserId1);
        }
        catch (Exception)
        {
            // GetTaskByIdAsync may fail in mock we're testing scheduling, not the return
        }

        _mockBackgroundJob.Verify(
            b => b.ScheduleTaskDueSoonNotification(It.IsAny<Guid>(), UserId2, dueDate),
            Times.Once);
    }


    [Fact]
    public async Task ChangeTaskStatusAsync_WhenNotMember_ThrowsUnauthorized()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>()).Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.ChangeTaskStatusAsync(TaskId1, new ChangeTaskStatusDto { NewStatusId = 2 }, UserId2));
    }

    [Fact]
    public async Task ChangeTaskStatusAsync_RegularMemberNotAssignedOrCreator_ThrowsUnauthorized()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, createdBy: UserId1, assignedToId: UserId2);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);

        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId3, GroupId1, roleId: RoleIds.Member)
        };
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.ChangeTaskStatusAsync(TaskId1, new ChangeTaskStatusDto { NewStatusId = 2 }, UserId3));
    }

    [Fact]
    public async Task ChangeTaskStatusAsync_AssignedUserCanChangeStatus()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, createdBy: UserId1, assignedToId: UserId2, statusId: (int)TaskStatusItem.NotStarted);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);

        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId2, GroupId1, roleId: RoleIds.Member)
        };
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);
        _mockContext.Setup(c => c.TaskStatuses).Returns(MockDbSetFactory.Create(CreateStatuses()).Object);
        _mockContext.Setup(c => c.TaskPriorities).Returns(MockDbSetFactory.Create(CreatePriorities()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _sut.ChangeTaskStatusAsync(TaskId1, new ChangeTaskStatusDto { NewStatusId = (int)TaskStatusItem.InProgress }, UserId2);

        Assert.Equal("Not Started", result.OldStatus);
        Assert.Equal("In Progress", result.NewStatus);
    }

    [Fact]
    public async Task ChangeTaskStatusAsync_ToCompleted_SetsCompletedAt()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, createdBy: UserId1, statusId: (int)TaskStatusItem.InProgress);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);

        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.TeamLead)
        };
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);
        _mockContext.Setup(c => c.TaskStatuses).Returns(MockDbSetFactory.Create(CreateStatuses()).Object);
        _mockContext.Setup(c => c.TaskPriorities).Returns(MockDbSetFactory.Create(CreatePriorities()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _sut.ChangeTaskStatusAsync(TaskId1, new ChangeTaskStatusDto { NewStatusId = (int)TaskStatusItem.Completed }, UserId1);

        Assert.NotNull(task.CompletedAt);
        Assert.Equal("Completed", result.NewStatus);
    }

    [Fact]
    public async Task ChangeTaskStatusAsync_FromCompletedToInProgress_ClearsCompletedAt()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, createdBy: UserId1,
            statusId: (int)TaskStatusItem.Completed, completedAt: DateTime.UtcNow.AddDays(-1));

        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.TeamLead)
        }).Object);
        _mockContext.Setup(c => c.TaskStatuses).Returns(MockDbSetFactory.Create(CreateStatuses()).Object);
        _mockContext.Setup(c => c.TaskPriorities).Returns(MockDbSetFactory.Create(CreatePriorities()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await _sut.ChangeTaskStatusAsync(TaskId1, new ChangeTaskStatusDto { NewStatusId = (int)TaskStatusItem.InProgress }, UserId1);

        Assert.Null(task.CompletedAt);
    }


    [Theory]
    [InlineData(RoleIds.Member, false)]   
    [InlineData(RoleIds.TeamLead, false)] 
    [InlineData(RoleIds.Manager, true)]    
    [InlineData(RoleIds.Owner, true)]      
    public async Task DeleteTaskAsync_RequiresManagerOrAbove(int roleId, bool shouldSucceed)
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1);
        task.Attachments = new List<TaskAttachment>();

        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: roleId)
        }).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        if (shouldSucceed)
        {
            await _sut.DeleteTaskAsync(TaskId1, UserId1);
            Assert.True(task.IsDeleted);
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => _sut.DeleteTaskAsync(TaskId1, UserId1));
        }
    }

    [Theory]
    [InlineData(RoleIds.Member, false)]  
    [InlineData(RoleIds.TeamLead, true)]   
    [InlineData(RoleIds.Manager, true)]    
    public async Task AssignTaskAsync_RequiresTeamLeadOrAbove(int roleId, bool shouldSucceed)
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);

        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: roleId),
            CreateMembership(UserId2, GroupId1, roleId: RoleIds.Member)
        };
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var dto = new AssignTaskDto { UserId = UserId2 };

        if (shouldSucceed)
        {
            await _sut.AssignTaskAsync(TaskId1, dto, UserId1);
            Assert.Equal(UserId2, task.AssignedToId);
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => _sut.AssignTaskAsync(TaskId1, dto, UserId1));
        }
    }

    [Fact]
    public async Task UnassignTaskAsync_AssignedUserCanUnassignThemselves()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, assignedToId: UserId2);
        task.Attachments = new List<TaskAttachment>();

        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId2, GroupId1, roleId: RoleIds.Member)
        }).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await _sut.UnassignTaskAsync(TaskId1, UserId2);

        Assert.Null(task.AssignedToId);
    }

    [Fact]
    public async Task UnassignTaskAsync_UnrelatedMemberCannotUnassign()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, assignedToId: UserId2);
        task.Attachments = new List<TaskAttachment>();

        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId3, GroupId1, roleId: RoleIds.Member)
        }).Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.UnassignTaskAsync(TaskId1, UserId3));
    }


    [Fact]
    public async Task UpdateTaskAsync_TaskCreatorCanUpdateEvenAsMember()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, createdBy: UserId1);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        }).Object);
        _mockContext.Setup(c => c.TaskPriorities).Returns(MockDbSetFactory.Create(CreatePriorities()).Object);
        _mockContext.Setup(c => c.TaskStatuses).Returns(MockDbSetFactory.Create(CreateStatuses()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var dto = new UpdateTaskDto { Title = "Updated Title" };

        try
        {
            await _sut.UpdateTaskAsync(TaskId1, dto, UserId1);
        }
        catch (InvalidOperationException) { }

        Assert.Equal("Updated Title", task.Title);
    }

    [Fact]
    public async Task UpdateTaskAsync_NonCreatorMember_ThrowsUnauthorized()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, createdBy: UserId1);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId2, GroupId1, roleId: RoleIds.Member)
        }).Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.UpdateTaskAsync(TaskId1, new UpdateTaskDto { Title = "Hacked" }, UserId2));
    }

    [Fact]
    public async Task UpdateTaskAsync_SchedulesDueSoonAfterSave_NotBefore()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, createdBy: UserId1,
            assignedToId: UserId2, dueDate: DateTime.UtcNow.AddDays(5));

        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.TeamLead)
        }).Object);
        _mockContext.Setup(c => c.TaskPriorities).Returns(MockDbSetFactory.Create(CreatePriorities()).Object);
        _mockContext.Setup(c => c.TaskStatuses).Returns(MockDbSetFactory.Create(CreateStatuses()).Object);

        var saveCallOrder = 0;
        var scheduleCallOrder = 0;
        var callCounter = 0;

        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saveCallOrder = ++callCounter)
            .ReturnsAsync(1);

        _mockBackgroundJob.Setup(b => b.ScheduleTaskDueSoonNotification(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>()))
            .Callback(() => scheduleCallOrder = ++callCounter);

        try
        {
            await _sut.UpdateTaskAsync(TaskId1, new UpdateTaskDto { Title = "Updated" }, UserId1);
        }
        catch { }

        Assert.True(saveCallOrder > 0, "SaveChangesAsync was not called");
        Assert.True(scheduleCallOrder > saveCallOrder,
            $"Schedule (order {scheduleCallOrder}) must happen AFTER save (order {saveCallOrder})");
    }

    [Fact]
    public async Task DeleteTaskAsync_SoftDeletesTaskAndAttachments()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1);
        var attachment = new TaskAttachment
        {
            Id = Guid.NewGuid(),
            TaskId = TaskId1,
            FileName = "test.pdf",
            FilePath = "/uploads/test.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            CreatedBy = UserId1
        };
        task.Attachments = new List<TaskAttachment> { attachment };

        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Manager)
        }).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await _sut.DeleteTaskAsync(TaskId1, UserId1);

        Assert.True(task.IsDeleted);
        Assert.NotNull(task.DeletedAt);
        Assert.Equal(UserId1, task.DeletedBy);
        Assert.True(attachment.IsDeleted);
        Assert.NotNull(attachment.DeletedAt);
    }

    [Fact]
    public async Task DeleteTaskAsync_TaskNotFound_ThrowsKeyNotFound()
    {
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem>()).Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.DeleteTaskAsync(Guid.NewGuid(), UserId1));
    }



    [Fact]
    public async Task GetKanbanBoardAsync_NonMember_ThrowsUnauthorized()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>()).Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _sut.GetKanbanBoardAsync(GroupId1, UserId1));
    }

    [Fact]
    public async Task GetKanbanBoardAsync_ReturnsClomunsWithTasksSorted()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>()
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        }).Object);

        _mockContext.Setup(c => c.Groups).Returns(MockDbSetFactory.Create(new List<Group>
        {
            CreateGroup(GroupId1, "Dev Team")
        }).Object);
        _mockContext.Setup(c => c.TaskStatuses).Returns(MockDbSetFactory.Create(CreateStatuses()).Object);

        var tasks = new List<TaskItem>
        {
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.NotStarted, displayOrder: 1, title: "Task B"),
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.NotStarted, displayOrder: 0, title: "Task A"),
            CreateTask(groupId: GroupId1, statusId: (int)TaskStatusItem.InProgress, displayOrder: 0, title: "Task C"),
        };
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(tasks).Object);

        var board = await _sut.GetKanbanBoardAsync(GroupId1, UserId1);

        Assert.Equal(4, board.Columns.Count);
        Assert.Equal("Dev Team", board.GroupName);
        Assert.Equal(2, board.Columns.First(c => c.StatusId == (int)TaskStatusItem.NotStarted).Tasks.Count);
    }

    [Fact]
    public async Task MoveTaskAsync_AcrossColumns_SetsCompletedAtWhenMovingToCompleted()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1,
            statusId: (int)TaskStatusItem.InProgress, displayOrder: 0);

        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        }).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        await _sut.MoveTaskAsync(TaskId1, new MoveTaskDto
        {
            NewStatusId = (int)TaskStatusItem.Completed,
            NewDisplayOrder = 0
        }, UserId1);

        Assert.Equal((int)TaskStatusItem.Completed, task.StatusId);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public async Task MoveTaskAsync_ConcurrencyCOnflict_RetriesAndThrowsAfterMaxRetries()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, statusId: (int)TaskStatusItem.NotStarted);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, RoleIds.Member)
        }).Object);

        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("Concurrent modification"));

        _mockContext.Setup(c => c.ClearChangeTracker());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.MoveTaskAsync(TaskId1, new MoveTaskDto
            {
                NewStatusId = (int)TaskStatusItem.InProgress,
                NewDisplayOrder = 0
            }, UserId1));

        Assert.Contains("modified by another user", ex.Message);
    }
}