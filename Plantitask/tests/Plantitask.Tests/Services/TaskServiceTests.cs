using Azure.Core.GeoJson;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Org.BouncyCastle.Pqc.Crypto.Lms;
using Plantitask.Core.DTO.Kanban;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;


namespace Plantitask.Tests.Services
{
    public class TaskServiceTests : DbTestBase
    {
        private static readonly Guid TaskId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

        private readonly Mock<IBackgroundJobService> _jobs = new();

        public TaskServiceTests(PostgresFixture fixture) : base (fixture) { }

        private TaskService NewSut(IApplicationDbContext context) => new(
            context,
            NullLogger<TaskService>.Instance,
            new GroupService(
                context,
                Mock.Of<IGroupCodeGenerator>(),
                Mock.Of<IPasswordHasher>(),
                NullLogger<GroupService>.Instance),
            new MemoryCache(new MemoryCacheOptions()),
            _jobs.Object);

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        private async Task PromoteMemberAsync(GroupRole role)
        {
            await using var db = NewContext();
            await db.SetRoleAsync(MemberId, role);
        }

        /// <summary>Seeds one task in Dev Team created by the lead, at the well known TaskId.</summary>
        private async Task SeedTaskAsync(
            TaskStatusItem status = TaskStatusItem.NotStarted,
            TaskPriority priority = TaskPriority.Medium,
            Guid? assignedTo = null,
            Guid? createdBy = null,
            DateTime? dueDate = null,
            DateTime? completedAt = null)
        {
            await using var db = NewContext();
            var task = TestData.Task(
                GroupId, createdBy ?? LeadId,
                status: status, priority: priority,
                assignedTo: assignedTo, dueDate: dueDate, id: TaskId);
            task.CompletedAt = completedAt;
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
        }

        private static CreateTaskDto NewTaskDto(
            string title = "Write the tests",
            int priorityId = (int)TaskPriority.Medium,
            Guid? assignedTo = null,
            DateTime? dueDate = null) => new()
            {
                Title = title,
                Description = "Description",
                PriorityId = priorityId,
                AssignedToUserId = assignedTo,
                DueDate = dueDate
            };

        [Fact]
        public async Task CreateTaskAsync_WhenCallerIsNotAMember_ReturnsForbidden()
        {
            await SeedAsync();
            await using var act = NewContext();
            var result = await NewSut(act).CreateTaskAsync(GroupId, NewTaskDto(), OutsiderId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Equal(0, await assert.Tasks.CountAsync());
        }

        [Theory]
        [InlineData(GroupRole.Member, false)]
        [InlineData(GroupRole.TeamLead, true)]
        [InlineData(GroupRole.Manager, true)]
        [InlineData(GroupRole.Owner, true)]
        public async Task CreateTaskAsync_RequiresTeamLeadOrAbove(GroupRole role, bool shouldSucceed)
        {
            await SeedAsync();
            await PromoteMemberAsync(role);

            await using var act = NewContext();
            var result = await NewSut(act).CreateTaskAsync(GroupId, NewTaskDto(), MemberId);

            Assert.Equal(shouldSucceed, result.IsSuccess);

            await using var assert = NewContext();
            Assert.Equal(shouldSucceed ? 1 : 0, await assert.Tasks.CountAsync());
        }

        [Fact]
        public async Task CreateTaskAsync_WithUnknownPriority_ReturnsBadRequest()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).CreateTaskAsync(GroupId, NewTaskDto(priorityId: 999), LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
            Assert.Contains("priority", result.Error.Message);
        }
        [Fact]
        public async Task CreateTaskAsync_WhenAssigneeBelongsToAnotherGroup_ReturnsBadRequest()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).CreateTaskAsync(
                GroupId, NewTaskDto(assignedTo: OtherLeadId), LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Equal(0, await assert.Tasks.CountAsync());
        }

        [Fact]
        public async Task CreateTaskAsync_AsTeamLead_PersistsTheTaskAndReturnsTheJoinedProjection()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).CreateTaskAsync(
                GroupId, NewTaskDto(assignedTo: MemberId), LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            var dto = result.Value!;
            Assert.Equal("Write the tests", dto.Title);
            Assert.Equal("Dev Team", dto.GroupName);
            Assert.Equal("lead", dto.CreatedByUserName);
            Assert.Equal("member", dto.AssignedToUserName);
            Assert.Equal("Not Started", dto.StatusDisplayName);
            Assert.Equal("Medium", dto.PriorityName);

            await using var assert = NewContext();
            var stored = await assert.Tasks.SingleAsync();
            Assert.Equal((int)TaskStatusItem.NotStarted, stored.StatusId);
            Assert.Equal(LeadId, stored.CreatedBy);
            Assert.Equal(MemberId, stored.AssignedToId);
            Assert.NotEqual(default, stored.CreatedAt);
        }

        [Fact]
        public async Task CreateTaskAsync_PlacesEachNewTaskAtTheBottomOfNotStarted()
        {
            await SeedAsync();

            await using var act = NewContext();
            var sut = NewSut(act);

            await sut.CreateTaskAsync(GroupId, NewTaskDto(title: "First"), LeadId);
            await sut.CreateTaskAsync(GroupId, NewTaskDto(title: "Second"), LeadId);
            await sut.CreateTaskAsync(GroupId, NewTaskDto(title: "Third"), LeadId);

            await using var assert = NewContext();
            var orders = await assert.Tasks
                .OrderBy(t => t.DisplayOrder)
                .Select(t => new { t.Title, t.DisplayOrder })
                .ToListAsync();

            Assert.Equal(new[] { "First", "Second", "Third" }, orders.Select(o => o.Title));
            Assert.Equal(new[] { 0, 1, 2 }, orders.Select(o => o.DisplayOrder));
        }

        [Fact]
        public async Task CreateTaskAsync_NumbersEachGroupsColumnIndependently()
        {
            await SeedAsync();

            await using var act = NewContext();
            var sut = NewSut(act);

            await sut.CreateTaskAsync(GroupId, NewTaskDto(title: "Ours"), LeadId);
            await sut.CreateTaskAsync(OtherGroupId, NewTaskDto(title: "Theirs"), OtherLeadId);

            await using var assert = NewContext();
            Assert.Equal(0, (await assert.Tasks.SingleAsync(t => t.GroupId == GroupId)).DisplayOrder);
            Assert.Equal(0, (await assert.Tasks.SingleAsync(t => t.GroupId == OtherGroupId)).DisplayOrder);
        }

        [Fact]
        public async Task CreateTaskAsync_WithDueDateAndAssignee_SchedulesTheDueSoonReminder()
        {
            await SeedAsync();

            var dueDate = DateTime.UtcNow.AddDays(3);
            _jobs.Setup(j => j.ScheduleTaskDueSoonNotification(It.IsAny<Guid>(), MemberId, dueDate))
                .ReturnsAsync("hangfire-job-1");

            await using var act = NewContext();
            var result = await NewSut(act).CreateTaskAsync(
                GroupId, NewTaskDto(assignedTo: MemberId, dueDate: dueDate), LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            _jobs.Verify(j => j.ScheduleTaskDueSoonNotification(
                It.IsAny<Guid>(), MemberId, dueDate), Times.Once);

            await using var assert = NewContext();
            Assert.Equal("hangfire-job-1", (await assert.Tasks.SingleAsync()).DueSoonJobId);
        }


        [Fact]
        public async Task CreateTaskAsync_WithNoAssignee_DoesNotScheduleAReminder()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).CreateTaskAsync(
                GroupId, NewTaskDto(dueDate: DateTime.UtcNow.AddDays(3)), LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            _jobs.Verify(j => j.ScheduleTaskDueSoonNotification(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>()), Times.Never);

            await using var assert = NewContext();
            Assert.Null((await assert.Tasks.SingleAsync()).DueSoonJobId);
        }

        [Fact]
        public async Task GetTaskByIdAsync_WhenTaskDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskByIdAsync(Guid.NewGuid(), LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task GetTaskByIdAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskByIdAsync(TaskId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task GetTaskByIdAsync_AsAnyMember_ReturnsTheProjection()
        {
            await SeedAsync();
            await SeedTaskAsync(assignedTo: MemberId, priority: TaskPriority.Urgent);

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskByIdAsync(TaskId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("Dev Team", result.Value!.GroupName);
            Assert.Equal("Urgent", result.Value.PriorityName);
            Assert.Equal("member", result.Value.AssignedToUserName);
            Assert.Equal(0, result.Value.AttachmentCount);
        }

        [Fact]
        public async Task GetGroupTasksAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupTasksAsync(GroupId, null, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task GetGroupTasksAsync_ReturnsOnlyTheRequestedGroupsTasks()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "Ours"));
                db.Tasks.Add(TestData.Task(OtherGroupId, OtherLeadId, title: "Theirs"));
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupTasksAsync(GroupId, null, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(1, result.Value!.TotalCount);
            Assert.Equal("Ours", result.Value.Items.Single().Title);
        }

        [Fact]
        public async Task GetGroupTasksAsync_ExcludesSoftDeletedTasks()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "Alive"));
                var dead = TestData.Task(GroupId, LeadId, title: "Deleted");
                dead.IsDeleted = true;
                dead.DeletedAt = DateTime.UtcNow;
                db.Tasks.Add(dead);
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupTasksAsync(GroupId, null, LeadId);

            Assert.Equal(1, result.Value!.TotalCount);
            Assert.Equal("Alive", result.Value.Items.Single().Title);
        }

        [Fact]
        public async Task GetGroupTasksAsync_FiltersByStatusAndPriorityAndAssignee()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "Match",
                    status: TaskStatusItem.InProgress, priority: TaskPriority.High, assignedTo: MemberId));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "WrongStatus",
                    status: TaskStatusItem.NotStarted, priority: TaskPriority.High, assignedTo: MemberId));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "WrongPriority",
                    status: TaskStatusItem.InProgress, priority: TaskPriority.Low, assignedTo: MemberId));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "WrongAssignee",
                    status: TaskStatusItem.InProgress, priority: TaskPriority.High, assignedTo: LeadId));
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupTasksAsync(GroupId, new TaskFilterDto
            {
                StatusId = (int)TaskStatusItem.InProgress,
                PriorityId = (int)TaskPriority.High,
                AssignedToUserId = MemberId
            }, LeadId);

            Assert.Equal(1, result.Value!.TotalCount);
            Assert.Equal("Match", result.Value.Items.Single().Title);
        }

        [Fact]
        public async Task GetGroupTasksAsync_OverdueFilterIgnoresCompletedTasks()
        {
            await SeedAsync();

            var past = DateTime.UtcNow.AddDays(-2);

            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "Overdue",
                    status: TaskStatusItem.InProgress, dueDate: past));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "OverdueButDone",
                    status: TaskStatusItem.Completed, dueDate: past));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "NotDueYet",
                    status: TaskStatusItem.InProgress, dueDate: DateTime.UtcNow.AddDays(5)));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "NoDueDate"));
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupTasksAsync(
                GroupId, new TaskFilterDto { IsOverDue = true }, LeadId);

            Assert.Equal(1, result.Value!.TotalCount);
            Assert.Equal("Overdue", result.Value.Items.Single().Title);
        }

        [Fact]
        public async Task GetGroupTasksAsync_SearchTermMatchesTitleOrDescriptionCaseInsensitively()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                var byTitle = TestData.Task(GroupId, LeadId, title: "Deploy The PIPELINE");
                byTitle.Description = "nothing relevant";

                var byDescription = TestData.Task(GroupId, LeadId, title: "Unrelated");
                byDescription.Description = "touches the pipeline config";

                var noMatch = TestData.Task(GroupId, LeadId, title: "Unrelated too");
                noMatch.Description = "nothing relevant";

                db.Tasks.AddRange(byTitle, byDescription, noMatch);
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupTasksAsync(
                GroupId, new TaskFilterDto { SearchTerm = "pipeline" }, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(2, result.Value!.TotalCount);
            Assert.DoesNotContain(result.Value.Items, t => t.Title == "Unrelated too");
        }

        [Fact]
        public async Task GetGroupTasksAsync_PagesAndCountsBeforeSkipTake()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                for (var i = 0; i < 7; i++)
                    db.Tasks.Add(TestData.Task(GroupId, LeadId, title: $"Task {i}"));
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetGroupTasksAsync(GroupId, null, LeadId, pageNumber: 2, pageSize: 3);

            Assert.Equal(7, result.Value!.TotalCount);
            Assert.Equal(3, result.Value.Items.Count);
            Assert.Equal(2, result.Value.PageNumber);
        }

        [Fact]
        public async Task UpdateTaskAsync_WhenTaskDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UpdateTaskAsync(
                Guid.NewGuid(), new UpdateTaskDto { Title = "New" }, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task UpdateTaskAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UpdateTaskAsync(
                TaskId, new UpdateTaskDto { Title = "Hijacked" }, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            await using var assert = NewContext();
            Assert.NotEqual("Hijacked", (await assert.Tasks.SingleAsync()).Title);
        }

        [Fact]
        public async Task UpdateTaskAsync_WhenPlainMemberDidNotCreateIt_ReturnsForbidden()
        {
            await SeedAsync();
            await SeedTaskAsync(createdBy: LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).UpdateTaskAsync(
                TaskId, new UpdateTaskDto { Title = "Hijacked" }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task UpdateTaskAsync_WhenPlainMemberCreatedIt_Succeeds()
        {
            await SeedAsync();
            await SeedTaskAsync(createdBy: MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).UpdateTaskAsync(
                TaskId, new UpdateTaskDto { Title = "Mine to edit" }, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            var stored = await assert.Tasks.SingleAsync();
            Assert.Equal("Mine to edit", stored.Title);
            Assert.Equal(MemberId, stored.UpdatedBy);
            Assert.NotNull(stored.UpdatedAt);
        }

        [Fact]
        public async Task UpdateTaskAsync_NullDescriptionLeavesItAloneAndWhitespaceClearsIt()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using (var act = NewContext())
            {
                await NewSut(act).UpdateTaskAsync(TaskId, new UpdateTaskDto { Title = "Renamed" }, LeadId);
            }

            await using (var assert = NewContext())
            {
                Assert.Equal("Seeded description", (await assert.Tasks.SingleAsync()).Description);
            }

            await using (var act = NewContext())
            {
                await NewSut(act).UpdateTaskAsync(TaskId, new UpdateTaskDto { Description = "   " }, LeadId);
            }

            await using (var assert = NewContext())
            {
                Assert.Null((await assert.Tasks.SingleAsync()).Description);
            }
        }

        [Fact]
        public async Task UpdateTaskAsync_ClearDueDateWinsOverAnySuppliedDate()
        {
            await SeedAsync();
            await SeedTaskAsync(dueDate: DateTime.UtcNow.AddDays(4));

            await using var act = NewContext();
            var result = await NewSut(act).UpdateTaskAsync(TaskId, new UpdateTaskDto
            {
                DueDate = DateTime.UtcNow.AddDays(9),
                ClearDueDate = true
            }, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            Assert.Null((await assert.Tasks.SingleAsync()).DueDate);
        }

        [Fact]
        public async Task UpdateTaskAsync_WithUnknownPriority_ReturnsBadRequestAndSavesNothing()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UpdateTaskAsync(
                TaskId, new UpdateTaskDto { Title = "Renamed", PriorityId = 999 }, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Equal("Seeded task", (await assert.Tasks.SingleAsync()).Title);
        }

        [Fact]
        public async Task UpdateTaskAsync_CancelsTheOldReminderBeforeSchedulingTheNewOne()
        {
            await SeedAsync();
            await SeedTaskAsync(assignedTo: MemberId, dueDate: DateTime.UtcNow.AddDays(2));

            await using (var db = NewContext())
            {
                (await db.Tasks.SingleAsync()).DueSoonJobId = "old-job";
                await db.SaveChangesAsync();
            }

            var newDueDate = DateTime.UtcNow.AddDays(6);
            _jobs.Setup(j => j.ScheduleTaskDueSoonNotification(TaskId, MemberId, newDueDate))
                .ReturnsAsync("new-job");

            await using var act = NewContext();
            var result = await NewSut(act).UpdateTaskAsync(
                TaskId, new UpdateTaskDto { DueDate = newDueDate }, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            _jobs.Verify(j => j.CancelScheduledJob("old-job"), Times.Once);

            await using var assert = NewContext();
            Assert.Equal("new-job", (await assert.Tasks.SingleAsync()).DueSoonJobId);
        }

        [Fact]
        public async Task ChangeTaskStatusAsync_WhenTaskDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskStatusAsync(
                Guid.NewGuid(), new ChangeTaskStatusDto { NewStatusId = 2 }, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task ChangeTaskStatusAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskStatusAsync(
                TaskId, new ChangeTaskStatusDto { NewStatusId = 2 }, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task ChangeTaskStatusAsync_WhenPlainMemberIsNeitherAssigneeNorCreator_ReturnsForbidden()
        {
            await SeedAsync();
            await SeedTaskAsync(createdBy: LeadId, assignedTo: LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskStatusAsync(
                TaskId, new ChangeTaskStatusDto { NewStatusId = 2 }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task ChangeTaskStatusAsync_TheAssigneeMayChangeItEvenAsAPlainMember()
        {
            await SeedAsync();
            await SeedTaskAsync(createdBy: LeadId, assignedTo: MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskStatusAsync(
                TaskId, new ChangeTaskStatusDto { NewStatusId = (int)TaskStatusItem.InProgress }, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("Not Started", result.Value!.OldStatus);
            Assert.Equal("In Progress", result.Value.NewStatus);
        }

        [Fact]
        public async Task ChangeTaskStatusAsync_TheCreatorMayChangeItEvenAsAPlainMember()
        {
            await SeedAsync();
            await SeedTaskAsync(createdBy: MemberId, assignedTo: LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskStatusAsync(
                TaskId, new ChangeTaskStatusDto { NewStatusId = (int)TaskStatusItem.InProgress }, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
        }

        [Fact]
        public async Task ChangeTaskStatusAsync_WithUnknownStatus_ReturnsBadRequest()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskStatusAsync(
                TaskId, new ChangeTaskStatusDto { NewStatusId = 999 }, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        [Fact]
        public async Task ChangeTaskStatusAsync_ToTheStatusItAlreadyHas_ReturnsBadRequest()
        {
            await SeedAsync();
            await SeedTaskAsync(status: TaskStatusItem.InProgress);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskStatusAsync(
                TaskId, new ChangeTaskStatusDto { NewStatusId = (int)TaskStatusItem.InProgress }, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }

        [Fact]
        public async Task ChangeTaskStatusAsync_EnteringCompletedStampsCompletedAt()
        {
            await SeedAsync();
            await SeedTaskAsync(status: TaskStatusItem.InProgress);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskStatusAsync(
                TaskId, new ChangeTaskStatusDto { NewStatusId = (int)TaskStatusItem.Completed }, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            Assert.NotNull((await assert.Tasks.SingleAsync()).CompletedAt);
        }

        [Fact]
        public async Task ChangeTaskStatusAsync_LeavingCompletedClearsCompletedAt()
        {
            await SeedAsync();
            await SeedTaskAsync(status: TaskStatusItem.Completed, completedAt: DateTime.UtcNow.AddDays(-1));

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskStatusAsync(
                TaskId, new ChangeTaskStatusDto { NewStatusId = (int)TaskStatusItem.InProgress }, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            Assert.Null((await assert.Tasks.SingleAsync()).CompletedAt);
        }

        [Fact]
        public async Task ChangeTaskStatusAsync_MovesTheTaskToTheBottomOfItsNewColumn()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "Already there",
                    status: TaskStatusItem.InProgress, displayOrder: 0));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "Also there",
                    status: TaskStatusItem.InProgress, displayOrder: 1));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "Moving",
                    status: TaskStatusItem.NotStarted, displayOrder: 0, id: TaskId));
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            await NewSut(act).ChangeTaskStatusAsync(
                TaskId, new ChangeTaskStatusDto { NewStatusId = (int)TaskStatusItem.InProgress }, LeadId);

            await using var assert = NewContext();
            var moved = await assert.Tasks.SingleAsync(t => t.Id == TaskId);
            Assert.Equal(2, moved.DisplayOrder);
        }

        [Theory]
        [InlineData(GroupRole.Member, false)]
        [InlineData(GroupRole.TeamLead, true)]
        [InlineData(GroupRole.Manager, true)]
        public async Task ChangeTaskPriorityAsync_RequiresTeamLeadOrAbove(GroupRole role, bool shouldSucceed)
        {
            await SeedAsync();
            await PromoteMemberAsync(role);
            await SeedTaskAsync(priority: TaskPriority.Low, createdBy: MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskPriorityAsync(
                TaskId, (int)TaskPriority.Urgent, MemberId);

            Assert.Equal(shouldSucceed, result.IsSuccess);

            await using var assert = NewContext();
            var expected = shouldSucceed ? TaskPriority.Urgent : TaskPriority.Low;
            Assert.Equal((int)expected, (await assert.Tasks.SingleAsync()).PriorityId);
        }

        [Fact]
        public async Task ChangeTaskPriorityAsync_ReturnsBothDisplayNames()
        {
            await SeedAsync();
            await SeedTaskAsync(priority: TaskPriority.Low);

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskPriorityAsync(
                TaskId, (int)TaskPriority.Urgent, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("Low", result.Value!.OldPriority);
            Assert.Equal("Urgent", result.Value.NewPriority);
        }

        [Fact]
        public async Task ChangeTaskPriorityAsync_WithUnknownPriority_ReturnsBadRequest()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).ChangeTaskPriorityAsync(TaskId, 999, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);
        }
        [Theory]
        [InlineData(GroupRole.Member, false)]
        [InlineData(GroupRole.TeamLead, true)]
        [InlineData(GroupRole.Manager, true)]
        public async Task AssignTaskAsync_RequiresTeamLeadOrAbove(GroupRole role, bool shouldSucceed)
        {
            await SeedAsync();
            await PromoteMemberAsync(role);
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).AssignTaskAsync(
                TaskId, new AssignTaskDto { UserId = LeadId }, MemberId);

            Assert.Equal(shouldSucceed, result.IsSuccess);

            await using var assert = NewContext();
            var stored = await assert.Tasks.SingleAsync();
            Assert.Equal(shouldSucceed ? LeadId : null, stored.AssignedToId);
        }

        [Fact]
        public async Task AssignTaskAsync_WhenTargetBelongsToAnotherGroup_ReturnsBadRequest()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).AssignTaskAsync(
                TaskId, new AssignTaskDto { UserId = OtherLeadId }, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("BadRequest", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Null((await assert.Tasks.SingleAsync()).AssignedToId);
        }

        [Fact]
        public async Task UnassignTaskAsync_TheAssigneeMayRemoveThemselves()
        {
            await SeedAsync();
            await SeedTaskAsync(assignedTo: MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).UnassignTaskAsync(TaskId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            Assert.Null((await assert.Tasks.SingleAsync()).AssignedToId);
        }

        [Fact]
        public async Task UnassignTaskAsync_AnUninvolvedPlainMemberCannot()
        {
            await SeedAsync();
            await SeedTaskAsync(assignedTo: LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).UnassignTaskAsync(TaskId, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Equal(LeadId, (await assert.Tasks.SingleAsync()).AssignedToId);
        }

        [Theory]
        [InlineData(GroupRole.Member, false)]
        [InlineData(GroupRole.TeamLead, false)]
        [InlineData(GroupRole.Manager, true)]
        [InlineData(GroupRole.Owner, true)]
        public async Task DeleteTaskAsync_RequiresManagerOrAbove(GroupRole role, bool shouldSucceed)
        {
            await SeedAsync();
            await PromoteMemberAsync(role);
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).DeleteTaskAsync(TaskId, MemberId);

            Assert.Equal(shouldSucceed, result.IsSuccess);

            await using var assert = NewContext();
            Assert.Equal(shouldSucceed ? 0 : 1, await assert.Tasks.CountAsync());
        }

        [Fact]
        public async Task DeleteTaskAsync_WhenTaskDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).DeleteTaskAsync(Guid.NewGuid(), LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task DeleteTaskAsync_SoftDeletesTheTaskAndCascadesToItsChildren()
        {
            await SeedAsync();
            await PromoteMemberAsync(GroupRole.Manager);
            await SeedTaskAsync();

            await using (var db = NewContext())
            {
                db.TaskComments.Add(new TaskComment
                {
                    TaskId = TaskId,
                    Content = "a comment",
                    CreatedBy = LeadId
                });
                db.TaskAttachments.Add(new TaskAttachment
                {
                    TaskId = TaskId,
                    FileName = "spec.pdf",
                    FilePath = "uploads/spec.pdf",
                    ContentType = "application/pdf",
                    FileSize = 1024,
                    CreatedBy = LeadId
                });
                db.Notifications.Add(new Notification
                {
                    UserId = LeadId,
                    Type = NotificationType.TaskAssigned,
                    Title = "Assigned",
                    Message = "You were assigned",
                    RelatedEntityId = TaskId,
                    RelatedEntityType = "Task"
                });
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).DeleteTaskAsync(TaskId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();

            Assert.Equal(0, await assert.Tasks.CountAsync());
            Assert.Equal(0, await assert.TaskComments.CountAsync());
            Assert.Equal(0, await assert.TaskAttachments.CountAsync());
            Assert.Equal(0, await assert.Notifications.CountAsync());

            var task = await assert.Tasks.IgnoreQueryFilters().SingleAsync();
            Assert.True(task.IsDeleted);
            Assert.NotNull(task.DeletedAt);
            Assert.Equal(MemberId, task.DeletedBy);

            var comment = await assert.TaskComments.IgnoreQueryFilters().SingleAsync();
            Assert.True(comment.IsDeleted);
            Assert.Equal(MemberId, comment.DeletedBy);
            Assert.NotNull(comment.UpdatedAt);
        }

        [Fact]
        public async Task GetTaskGroupMembersAsync_ReturnsOnlyThatGroupsMembers()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskGroupMembersAsync(TaskId, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(2, result.Value!.Count);
            Assert.Contains(LeadId, result.Value);
            Assert.Contains(MemberId, result.Value);
            Assert.DoesNotContain(OtherLeadId, result.Value);
        }

        [Fact]
        public async Task GetTaskGroupMembersAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskGroupMembersAsync(TaskId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task GetKanbanBoardAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetKanbanBoardAsync(GroupId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task GetKanbanBoardAsync_ReturnsEveryStatusAsAColumnEvenWhenEmpty()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetKanbanBoardAsync(GroupId, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("Dev Team", result.Value!.GroupName);
            Assert.Equal(4, result.Value.Columns.Count);
            Assert.All(result.Value.Columns, c => Assert.Empty(c.Tasks));
        }

        [Fact]
        public async Task GetKanbanBoardAsync_PutsEachTaskInItsColumnOrderedAndIgnoresOtherGroups()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "Second",
                    status: TaskStatusItem.NotStarted, displayOrder: 1));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "First",
                    status: TaskStatusItem.NotStarted, displayOrder: 0));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "Doing",
                    status: TaskStatusItem.InProgress, displayOrder: 0));
                db.Tasks.Add(TestData.Task(OtherGroupId, OtherLeadId, title: "Theirs",
                    status: TaskStatusItem.NotStarted, displayOrder: 0));
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetKanbanBoardAsync(GroupId, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            var notStarted = result.Value!.Columns.Single(c => c.StatusId == (int)TaskStatusItem.NotStarted);
            Assert.Equal(new[] { "First", "Second" }, notStarted.Tasks.Select(t => t.Title));

            var inProgress = result.Value.Columns.Single(c => c.StatusId == (int)TaskStatusItem.InProgress);
            Assert.Equal("Doing", inProgress.Tasks.Single().Title);

            Assert.DoesNotContain(result.Value.Columns.SelectMany(c => c.Tasks), t => t.Title == "Theirs");
        }

        [Fact]
        public async Task MoveTaskAsync_WhenTaskDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).MoveTaskAsync(Guid.NewGuid(), new MoveTaskDto
            {
                NewStatusId = (int)TaskStatusItem.InProgress,
                NewDisplayOrder = 0
            }, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task MoveTaskAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using var act = NewContext();
            var result = await NewSut(act).MoveTaskAsync(TaskId, new MoveTaskDto
            {
                NewStatusId = (int)TaskStatusItem.InProgress,
                NewDisplayOrder = 0
            }, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task MoveTaskAsync_APlainMemberMayReorderInsideAColumn()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "A", displayOrder: 0, id: TaskId));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "B", displayOrder: 1));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "C", displayOrder: 2));
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).MoveTaskAsync(TaskId, new MoveTaskDto
            {
                NewStatusId = (int)TaskStatusItem.NotStarted,
                NewDisplayOrder = 2
            }, MemberId);

            await using var assert = NewContext();
            var order = await assert.Tasks
                .OrderBy(t => t.DisplayOrder)
                .Select(t => t.Title)
                .ToListAsync();

            Assert.Equal(new[] { "B", "C", "A" }, order);
        }

        [Fact]

        public async Task MoveTaskAsync_APlainMembeMayNotDragSomeoneElsesTaskToAnotherColumn()
        {
            await SeedAsync();
            await SeedTaskAsync(createdBy: LeadId, assignedTo: LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).MoveTaskAsync(TaskId, new MoveTaskDto
            {
                NewStatusId = (int)TaskStatusItem.InProgress,
                NewDisplayOrder = 0
            }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Equal((int)TaskStatusItem.NotStarted, (await assert.Tasks.SingleAsync()).StatusId);
        }

        [Fact]
        public async Task MoveTaskAsync_DraggingIntoCompletedStampsCompletedAt()
        {
            await SeedAsync();
            await SeedTaskAsync(status: TaskStatusItem.InProgress);

            await using var act = NewContext();
            var result = await NewSut(act).MoveTaskAsync(TaskId, new MoveTaskDto
            {
                NewStatusId = (int)TaskStatusItem.Completed,
                NewDisplayOrder = 0
            }, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            var moved = await assert.Tasks.SingleAsync();
            Assert.Equal((int)TaskStatusItem.Completed, moved.StatusId);
            Assert.NotNull(moved.CompletedAt);
        }

        [Fact]
        public async Task MoveTaskAsync_DraggingOutOfCompletedClearsCompletedAt()
        {
            await SeedAsync();
            await SeedTaskAsync(status: TaskStatusItem.Completed, completedAt: DateTime.UtcNow.AddDays(-1));

            await using var act = NewContext();
            var result = await NewSut(act).MoveTaskAsync(TaskId, new MoveTaskDto
            {
                NewStatusId = (int)TaskStatusItem.InProgress,
                NewDisplayOrder = 0
            }, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            Assert.Null((await assert.Tasks.SingleAsync()).CompletedAt);
        }

        [Fact]
        public async Task MoveTaskAsync_AcrossColumnsClosesTheGapInTheSourceColumn()
        {
            await SeedAsync();

            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "A", displayOrder: 0));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "B", displayOrder: 1, id: TaskId));
                db.Tasks.Add(TestData.Task(GroupId, LeadId, title: "C", displayOrder: 2));
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).MoveTaskAsync(TaskId, new MoveTaskDto
            {
                NewStatusId = (int)TaskStatusItem.InProgress,
                NewDisplayOrder = 0
            }, LeadId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            var remaining = await assert.Tasks
                .Where(t => t.StatusId == (int)TaskStatusItem.NotStarted)
                .OrderBy(t => t.DisplayOrder)
                .Select(t => new { t.Title, t.DisplayOrder })
                .ToListAsync();

            Assert.Equal(new[] { "A", "C" }, remaining.Select(r => r.Title));
            Assert.Equal(new[] { 0, 1 }, remaining.Select(r => r.DisplayOrder));
        }

        /// <summary>
        /// The xmin concurrency token is what MoveTaskAsync's retry loop reacts to. This proves the
        /// token is live on a real Postgres connection. The loop's give-up path needs sustained
        /// contention across three attempts and is not deterministically reachable from a test.
        /// </summary>
        [Fact]
        public async Task TaskItem_RowVersion_DetectsAConcurrentWrite()
        {
            await SeedAsync();
            await SeedTaskAsync();

            await using var first = NewContext();
            await using var second = NewContext();

            var readByFirst = await first.Tasks.SingleAsync();
            var readBySecond = await second.Tasks.SingleAsync();

            readByFirst.Title = "written by first caller";
            await first.SaveChangesAsync();

            readBySecond.Title = "written by second caller";

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        }
    }
}
