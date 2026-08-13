using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Plantitask.Core.DTO.Comments;
using Plantitask.Core.Entities;
using Plantitask.Core.Enums;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class CommentServiceTests : DbTestBase
    {
        public CommentServiceTests(PostgresFixture fixture) : base(fixture) { }

        private CommentService NewSut(IApplicationDbContext context) => new(
            context,
            NullLogger<CommentService>.Instance,
            new GroupService(
                context,
                Mock.Of<IGroupCodeGenerator>(),
                Mock.Of<IPasswordHasher>(),
                NullLogger<GroupService>.Instance));

        /// <summary>The seeded world plus one task in Dev Team at the well known TaskId.</summary>
        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
            db.Tasks.Add(TestData.Task(GroupId, LeadId, id: TaskId));
            await db.SaveChangesAsync();
        }

        private async Task<Guid> SeedCommentAsync(
            Guid author, string content = "seeded comment", DateTime? createdAt = null)
        {
            await using var db = NewContext();

            var comment = new TaskComment
            {
                TaskId = TaskId,
                Content = content,
                CreatedBy = author
            };

            db.TaskComments.Add(comment);
            await db.SaveChangesAsync();

            if (createdAt.HasValue)
                await db.BackdateAsync<TaskComment>(comment.Id, createdAt.Value);

            return comment.Id;
        }

        private async Task RemoveFromGroupAsync(Guid userId)
        {
            await using var db = NewContext();
            var membership = await db.GroupMembers
                .SingleAsync(gm => gm.GroupId == GroupId && gm.UserId == userId);
            db.GroupMembers.Remove(membership);
            await db.SaveChangesAsync();
        }

        private async Task PromoteMemberAsync(GroupRole role)
        {
            await using var db = NewContext();
            await db.SetRoleAsync(MemberId, role);
        }


        [Fact]
        public async Task AddCommentAsync_WhenTheTaskDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).AddCommentAsync(
                Guid.NewGuid(), new CreateCommentDto { Content = "hello" }, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task AddCommentAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).AddCommentAsync(
                TaskId, new CreateCommentDto { Content = "hello" }, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Equal(0, await assert.TaskComments.CountAsync());
        }

        [Fact]
        public async Task AddCommentAsync_AsAPlainMember_PersistsAndReturnsTheAuthorsName()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).AddCommentAsync(
                TaskId, new CreateCommentDto { Content = "looks good to me" }, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            var dto = result.Value!;
            Assert.Equal("looks good to me", dto.Content);
            Assert.Equal(TaskId, dto.TaskId);
            Assert.Equal(MemberId, dto.UserId);
            Assert.Equal("member", dto.UserName);
            Assert.False(dto.IsEdited);

            await using var assert = NewContext();
            var stored = await assert.TaskComments.SingleAsync();
            Assert.Equal(MemberId, stored.CreatedBy);
            Assert.NotEqual(default, stored.CreatedAt);
            Assert.Null(stored.UpdatedAt);
        }

        [Fact]
        public async Task GetTaskCommentsAsync_WhenTheTaskDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskCommentsAsync(Guid.NewGuid(), LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task GetTaskCommentsAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();
            await SeedCommentAsync(LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskCommentsAsync(TaskId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);
        }

        [Fact]
        public async Task GetTaskCommentsAsync_ReturnsOnlyThatTasksComments()
        {
            await SeedAsync();
            await SeedCommentAsync(LeadId, content: "on this task");

            var otherTaskId = Guid.NewGuid();
            await using (var db = NewContext())
            {
                db.Tasks.Add(TestData.Task(GroupId, LeadId, id: otherTaskId, title: "Another"));
                await db.SaveChangesAsync();

                db.TaskComments.Add(new TaskComment
                {
                    TaskId = otherTaskId,
                    Content = "on a different task",
                    CreatedBy = LeadId
                });
                await db.SaveChangesAsync();
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskCommentsAsync(TaskId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(1, result.Value!.TotalCount);
            Assert.Equal("on this task", result.Value.Items.Single().Content);
        }

        [Fact]
        public async Task GetTaskCommentsAsync_ExcludesSoftDeletedComments()
        {
            await SeedAsync();
            await SeedCommentAsync(LeadId, content: "alive");
            var doomedId = await SeedCommentAsync(LeadId, content: "deleted");

            await using (var del = NewContext())
            {
                await NewSut(del).DeleteCommentAsync(doomedId, LeadId);
            }

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskCommentsAsync(TaskId, LeadId);

            Assert.Equal(1, result.Value!.TotalCount);
            Assert.Equal("alive", result.Value.Items.Single().Content);
        }

        [Fact]
        public async Task GetTaskCommentsAsync_ReturnsNewestFirst()
        {
            await SeedAsync();
            var now = DateTime.UtcNow;
            await SeedCommentAsync(LeadId, content: "oldest", createdAt: now.AddHours(-3));
            await SeedCommentAsync(LeadId, content: "newest", createdAt: now);
            await SeedCommentAsync(LeadId, content: "middle", createdAt: now.AddHours(-1));

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskCommentsAsync(TaskId, LeadId);

            Assert.Equal(
                new[] { "newest", "middle", "oldest" },
                result.Value!.Items.Select(c => c.Content));
        }

        [Fact]
        public async Task GetTaskCommentsAsync_PagesAndCountsBeforeSkipTake()
        {
            await SeedAsync();
            var now = DateTime.UtcNow;
            for (var i = 0; i < 7; i++)
                await SeedCommentAsync(LeadId, content: $"comment {i}", createdAt: now.AddMinutes(-i));

            await using var act = NewContext();
            var result = await NewSut(act).GetTaskCommentsAsync(TaskId, LeadId, pageNumber: 2, pageSize: 3);

            Assert.Equal(7, result.Value!.TotalCount);
            Assert.Equal(new[] { "comment 3", "comment 4", "comment 5" },
                result.Value.Items.Select(c => c.Content));
        }



        [Fact]
        public async Task UpdateCommentAsync_WhenTheCommentDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).UpdateCommentAsync(
                Guid.NewGuid(), new UpdateCommentDto { Content = "edited" }, LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task UpdateCommentAsync_TheAuthorCanEditTheirOwn()
        {
            await SeedAsync();
            var commentId = await SeedCommentAsync(MemberId, content: "first draft");

            await using var act = NewContext();
            var result = await NewSut(act).UpdateCommentAsync(
                commentId, new UpdateCommentDto { Content = "second draft" }, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal("second draft", result.Value!.Content);
            Assert.True(result.Value.IsEdited);

            await using var assert = NewContext();
            var stored = await assert.TaskComments.SingleAsync();
            Assert.Equal("second draft", stored.Content);
            Assert.Equal(MemberId, stored.UpdatedBy);
            Assert.NotNull(stored.UpdatedAt);
        }

        /// <summary>
        /// Editing has no rank escape hatch. Delete extends to Managers and above, edit never
        /// does rewriting what someone else said is not a moderation power.
        /// </summary>
        [Theory]
        [InlineData(GroupRole.Member)]
        [InlineData(GroupRole.TeamLead)]
        [InlineData(GroupRole.Manager)]
        [InlineData(GroupRole.Owner)]
        public async Task UpdateCommentAsync_NoRankLetsYouEditSomeoneElsesComment(GroupRole role)
        {
            await SeedAsync();
            await PromoteMemberAsync(role);
            var commentId = await SeedCommentAsync(LeadId, content: "the lead wrote this");

            await using var act = NewContext();
            var result = await NewSut(act).UpdateCommentAsync(
                commentId, new UpdateCommentDto { Content = "tampered" }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Equal("the lead wrote this", (await assert.TaskComments.SingleAsync()).Content);
        }

        /// <summary>
        /// Authorship alone is not enough. Leaving the group ends your ability to touch what you
        /// wrote there which is the membership check that runs after the author check.
        /// </summary>
        [Fact]
        public async Task UpdateCommentAsync_TheAuthorCannotEditAfterLeavingTheGroup()
        {
            await SeedAsync();
            var commentId = await SeedCommentAsync(MemberId, content: "written while a member");
            await RemoveFromGroupAsync(MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).UpdateCommentAsync(
                commentId, new UpdateCommentDto { Content = "edited after leaving" }, MemberId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Equal("written while a member", (await assert.TaskComments.SingleAsync()).Content);
        }


        [Fact]
        public async Task DeleteCommentAsync_WhenTheCommentDoesNotExist_ReturnsNotFound()
        {
            await SeedAsync();

            await using var act = NewContext();
            var result = await NewSut(act).DeleteCommentAsync(Guid.NewGuid(), LeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("NotFound", result.Error!.Code);
        }

        [Fact]
        public async Task DeleteCommentAsync_WhenCallerLeadsAnotherGroup_ReturnsForbidden()
        {
            await SeedAsync();
            var commentId = await SeedCommentAsync(LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteCommentAsync(commentId, OtherLeadId);

            Assert.True(result.IsFailure);
            Assert.Equal("Forbidden", result.Error!.Code);

            await using var assert = NewContext();
            Assert.Equal(1, await assert.TaskComments.CountAsync());
        }

        [Fact]
        public async Task DeleteCommentAsync_TheAuthorCanDeleteTheirOwnAtAnyRank()
        {
            await SeedAsync();
            var commentId = await SeedCommentAsync(MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteCommentAsync(commentId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            Assert.Equal(0, await assert.TaskComments.CountAsync());
        }

        /// <summary>
        /// Someone else's comment needs Manager and above, so TeamLead is the failing side of
        /// the boundary and Manager the passing one.
        /// </summary>
        [Theory]
        [InlineData(GroupRole.Member, false)]
        [InlineData(GroupRole.TeamLead, false)]
        [InlineData(GroupRole.Manager, true)]
        [InlineData(GroupRole.Owner, true)]
        public async Task DeleteCommentAsync_SomeoneElsesNeedsManagerOrAbove(GroupRole role, bool shouldSucceed)
        {
            await SeedAsync();
            await PromoteMemberAsync(role);
            var commentId = await SeedCommentAsync(LeadId);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteCommentAsync(commentId, MemberId);

            Assert.Equal(shouldSucceed, result.IsSuccess);

            await using var assert = NewContext();
            Assert.Equal(shouldSucceed ? 0 : 1, await assert.TaskComments.CountAsync());
        }

        [Fact]
        public async Task DeleteCommentAsync_SoftDeletesRatherThanRemovingTheRow()
        {
            await SeedAsync();
            var commentId = await SeedCommentAsync(MemberId);

            await using var act = NewContext();
            var result = await NewSut(act).DeleteCommentAsync(commentId, MemberId);

            Assert.True(result.IsSuccess, result.Error?.Message);

            await using var assert = NewContext();
            Assert.Equal(0, await assert.TaskComments.CountAsync());

            var stored = await assert.TaskComments.IgnoreQueryFilters().SingleAsync();
            Assert.True(stored.IsDeleted);
            Assert.NotNull(stored.DeletedAt);
            Assert.Equal(MemberId, stored.DeletedBy);
            Assert.Equal("seeded comment", stored.Content);
        }
    }
}
