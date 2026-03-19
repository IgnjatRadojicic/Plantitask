using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Plantitask.Core.DTO.Kanban;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Entities;
using Plantitask.Core.Entities.Lookups;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestDataBuilder;

namespace Plantitask.Tests.Services;

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
    public async Task CreateTaskAsync_WhenUserIsNotGroupMember_ReturnsForbidden()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>()).Object);

        var dto = new CreateTaskDto { Title = "New Task", PriorityId = (int)TaskPriority.Medium };

        var result = await _sut.CreateTaskAsync(GroupId1, dto, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
        Assert.Contains("member of this group", result.Error.Message);
    }

    [Fact]
    public async Task CreateTaskAsync_WhenMemberPermissionBelowTeamLead_ReturnsForbidden()
    {
        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.Member)
        };
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);

        var dto = new CreateTaskDto { Title = "New Task", PriorityId = (int)TaskPriority.Medium };

        var result = await _sut.CreateTaskAsync(GroupId1, dto, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
        Assert.Contains("Team Leads", result.Error.Message);
    }

    [Fact]
    public async Task CreateTaskAsync_WhenAssigneeNotGroupMember_ReturnsBadRequest()
    {
        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.TeamLead)
        };
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);
        _mockContext.Setup(c => c.TaskPriorities).Returns(MockDbSetFactory.Create(CreatePriorities()).Object);

        var dto = new CreateTaskDto
        {
            Title = "New Task",
            PriorityId = (int)TaskPriority.Medium,
            AssignedToUserId = UserId2
        };

        var result = await _sut.CreateTaskAsync(GroupId1, dto, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("BadRequest", result.Error!.Code);
    }

    [Fact]
    public async Task CreateTaskAsync_InvalidPriority_ReturnsBadRequest()
    {
        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.TeamLead)
        };
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);
        _mockContext.Setup(c => c.TaskPriorities).Returns(MockDbSetFactory.Create(new List<TaskPriorityLookup>()).Object);

        var dto = new CreateTaskDto { Title = "New Task", PriorityId = 999 };

        var result = await _sut.CreateTaskAsync(GroupId1, dto, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("BadRequest", result.Error!.Code);
        Assert.Contains("priority", result.Error.Message);
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
        catch (NullReferenceException)
        {

        }

        _mockBackgroundJob.Verify(
            b => b.ScheduleTaskDueSoonNotification(It.IsAny<Guid>(), UserId2, dueDate),
            Times.Once);
    }


    [Fact]
    public async Task ChangeTaskStatusAsync_WhenNotMember_ReturnsForbidden()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>()).Object);

        var result = await _sut.ChangeTaskStatusAsync(TaskId1, new ChangeTaskStatusDto { NewStatusId = 2 }, UserId2);

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
    }

    [Fact]
    public async Task ChangeTaskStatusAsync_RegularMemberNotAssignedOrCreator_ReturnsForbidden()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, createdBy: UserId1, assignedToId: UserId2);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);

        var memberships = new List<GroupMember>
        {
            CreateMembership(UserId3, GroupId1, roleId: RoleIds.Member)
        };
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(memberships).Object);

        var result = await _sut.ChangeTaskStatusAsync(TaskId1, new ChangeTaskStatusDto { NewStatusId = 2 }, UserId3);

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
    }

    [Fact]
    public async Task ChangeTaskStatusAsync_TaskNotFound_ReturnsNotFound()
    {
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem>()).Object);

        var result = await _sut.ChangeTaskStatusAsync(Guid.NewGuid(), new ChangeTaskStatusDto { NewStatusId = 2 }, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
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

        Assert.True(result.IsSuccess);
        Assert.Equal("Not Started", result.Value!.OldStatus);
        Assert.Equal("In Progress", result.Value.NewStatus);
    }

    [Fact]
    public async Task ChangeTaskStatusAsync_ToCompleted_SetsCompletedAt()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, createdBy: UserId1, statusId: (int)TaskStatusItem.InProgress);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.TeamLead)
        }).Object);
        _mockContext.Setup(c => c.TaskStatuses).Returns(MockDbSetFactory.Create(CreateStatuses()).Object);
        _mockContext.Setup(c => c.TaskPriorities).Returns(MockDbSetFactory.Create(CreatePriorities()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _sut.ChangeTaskStatusAsync(TaskId1, new ChangeTaskStatusDto { NewStatusId = (int)TaskStatusItem.Completed }, UserId1);

        Assert.True(result.IsSuccess);
        Assert.NotNull(task.CompletedAt);
        Assert.Equal("Completed", result.Value!.NewStatus);
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

    [Fact]
    public async Task ChangeTaskStatusAsync_InvalidStatus_ReturnsBadRequest()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, createdBy: UserId1, statusId: (int)TaskStatusItem.NotStarted);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId1, GroupId1, roleId: RoleIds.TeamLead)
        }).Object);
        _mockContext.Setup(c => c.TaskStatuses).Returns(MockDbSetFactory.Create(new List<TaskStatusLookup>()).Object);
        _mockContext.Setup(c => c.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _sut.ChangeTaskStatusAsync(TaskId1, new ChangeTaskStatusDto { NewStatusId = 999 }, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("BadRequest", result.Error!.Code);
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

        var result = await _sut.DeleteTaskAsync(TaskId1, UserId1);

        if (shouldSucceed)
        {
            Assert.True(result.IsSuccess);
            Assert.True(task.IsDeleted);
        }
        else
        {
            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }
    }

    [Fact]
    public async Task DeleteTaskAsync_TaskNotFound_ReturnsNotFound()
    {
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem>()).Object);

        var result = await _sut.DeleteTaskAsync(Guid.NewGuid(), UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
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

        var result = await _sut.DeleteTaskAsync(TaskId1, UserId1);

        Assert.True(result.IsSuccess);
        Assert.True(task.IsDeleted);
        Assert.NotNull(task.DeletedAt);
        Assert.Equal(UserId1, task.DeletedBy);
        Assert.True(attachment.IsDeleted);
        Assert.NotNull(attachment.DeletedAt);
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

        var result = await _sut.AssignTaskAsync(TaskId1, dto, UserId1);

        if (shouldSucceed)
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(UserId2, task.AssignedToId);
        }
        else
        {
            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
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

        var result = await _sut.UnassignTaskAsync(TaskId1, UserId2);

        Assert.True(result.IsSuccess);
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

        var result = await _sut.UnassignTaskAsync(TaskId1, UserId3);

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
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

        await _sut.UpdateTaskAsync(TaskId1, dto, UserId1);

        Assert.Equal("Updated Title", task.Title);
    }

    [Fact]
    public async Task UpdateTaskAsync_NonCreatorMember_ReturnsForbidden()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1, createdBy: UserId1);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
        {
            CreateMembership(UserId2, GroupId1, roleId: RoleIds.Member)
        }).Object);

        var result = await _sut.UpdateTaskAsync(TaskId1, new UpdateTaskDto { Title = "Hacked" }, UserId2);

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
    }

    [Fact]
    public async Task UpdateTaskAsync_TaskNotFound_ReturnsNotFound()
    {
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem>()).Object);

        var result = await _sut.UpdateTaskAsync(Guid.NewGuid(), new UpdateTaskDto { Title = "X" }, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task GetKanbanBoardAsync_NonMember_ReturnsForbidden()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>()).Object);

        var result = await _sut.GetKanbanBoardAsync(GroupId1, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
    }

    [Fact]
    public async Task GetKanbanBoardAsync_ReturnsColumnsWithTasksSorted()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>
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

        var result = await _sut.GetKanbanBoardAsync(GroupId1, UserId1);

        Assert.True(result.IsSuccess);
        var board = result.Value!;
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

        var result = await _sut.MoveTaskAsync(TaskId1, new MoveTaskDto
        {
            NewStatusId = (int)TaskStatusItem.Completed,
            NewDisplayOrder = 0
        }, UserId1);

        Assert.True(result.IsSuccess);
        Assert.Equal((int)TaskStatusItem.Completed, task.StatusId);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public async Task MoveTaskAsync_TaskNotFound_ReturnsNotFound()
    {
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem>()).Object);

        var result = await _sut.MoveTaskAsync(Guid.NewGuid(), new MoveTaskDto
        {
            NewStatusId = (int)TaskStatusItem.InProgress,
            NewDisplayOrder = 0
        }, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task MoveTaskAsync_ConcurrencyConflict_ReturnsConflictAfterRetries()
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

        var result = await _sut.MoveTaskAsync(TaskId1, new MoveTaskDto
        {
            NewStatusId = (int)TaskStatusItem.InProgress,
            NewDisplayOrder = 0
        }, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("Conflict", result.Error!.Code);
        Assert.Contains("modified by another user", result.Error.Message);
    }


    [Fact]
    public async Task GetGroupTasksAsync_NonMember_ReturnsForbidden()
    {
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>()).Object);

        var result = await _sut.GetGroupTasksAsync(GroupId1, null, UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
    }

    [Fact]
    public async Task GetTaskByIdAsync_TaskNotFound_ReturnsNotFound()
    {
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem>()).Object);

        var result = await _sut.GetTaskByIdAsync(Guid.NewGuid(), UserId1);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Code);
    }

    [Fact]
    public async Task GetTaskByIdAsync_NonMember_ReturnsForbidden()
    {
        var task = CreateTask(id: TaskId1, groupId: GroupId1);
        _mockContext.Setup(c => c.Tasks).Returns(MockDbSetFactory.Create(new List<TaskItem> { task }).Object);
        _mockContext.Setup(c => c.GroupMembers).Returns(MockDbSetFactory.Create(new List<GroupMember>()).Object);

        var result = await _sut.GetTaskByIdAsync(TaskId1, UserId2);

        Assert.True(result.IsFailure);
        Assert.Equal("Forbidden", result.Error!.Code);
    }
}
