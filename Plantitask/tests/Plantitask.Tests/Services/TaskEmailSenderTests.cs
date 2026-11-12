using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Plantitask.Contracts.Enums;
using Plantitask.Core.Common;
using Plantitask.Core.Entities;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Data;
using Plantitask.Infrastructure.Services;
using Plantitask.Tests.Helpers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Services
{
    public class TaskEmailSenderTests : DbTestBase
    {
        private readonly Mock<IEmailService> _email = new();

        public TaskEmailSenderTests(PostgresFixture fixture) : base(fixture) { }

        private TaskEmailSender NewSut(IApplicationDbContext context) => new(
            new NotificationService(context, NullLogger<NotificationService>.Instance),
            _email.Object,
            NullLogger<TaskEmailSender>.Instance);

        private async Task SeedAsync()
        {
            await using var db = NewContext();
            await db.SeedWorldAsync();
        }

        private async Task TurnEmailOffAsync(Guid userId, NotificationType type)
        {
            await using var db = NewContext();
            db.NotificationPreferences.Add(new NotificationPreference
            {
                UserId = userId,
                Type = type,
                IsEnabled = true,
                IsEmailEnabled = false
            });
            await db.SaveChangesAsync();
        }

        private void TheMailProviderIsDown()
        {
            _email
                .Setup(e => e.SendTaskAssignmentEmailAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new EmailSendException("provider down"));

            _email
                .Setup(e => e.SendTaskCommentEmailAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new EmailSendException("provider down"));
        }

        [Fact]
        public async Task SendTaskAssignmentEmailAsync_SendsToTheAssignee()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).SendTaskAssignmentEmailAsync(MemberId, "Ship it", "Dev Team", "lead");

            _email.Verify(e => e.SendTaskAssignmentEmailAsync(
                "member@example.com", "member", "Ship it", "Dev Team", "lead"), Times.Once);
        }

        [Fact]
        public async Task SendTaskAssignmentEmailAsync_RespectsTheEmailPreference()
        {
            await SeedAsync();
            await TurnEmailOffAsync(MemberId, NotificationType.TaskAssigned);

            await using var act = NewContext();
            await NewSut(act).SendTaskAssignmentEmailAsync(MemberId, "Ship it", "Dev Team", "lead");

            _email.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The reason this class exists. A durable handler is marked done when it returns, so a
        /// mail failure has to come out as an exception or the email is never retried.
        /// </summary>
        [Fact]
        public async Task SendTaskAssignmentEmailAsync_LetsAFailedSendEscape()
        {
            await SeedAsync();
            TheMailProviderIsDown();

            await using var act = NewContext();
            await Assert.ThrowsAsync<EmailSendException>(
                () => NewSut(act).SendTaskAssignmentEmailAsync(MemberId, "Ship it", "Dev Team", "lead"));
        }

        [Fact]
        public async Task SendCommentEmailAsync_SendsToTheAssigneeNamingTheCommenter()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).SendCommentEmailAsync(MemberId, LeadId, "Ship it", "looks good");

            _email.Verify(e => e.SendTaskCommentEmailAsync(
                "member@example.com", "member", "lead", "Ship it", "looks good"), Times.Once);
        }

        [Fact]
        public async Task SendCommentEmailAsync_RespectsTheEmailPreference()
        {
            await SeedAsync();
            await TurnEmailOffAsync(MemberId, NotificationType.TaskCommentAdded);

            await using var act = NewContext();
            await NewSut(act).SendCommentEmailAsync(MemberId, LeadId, "Ship it", "looks good");

            _email.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendCommentEmailAsync_SaysNothingWhenYouCommentOnYourOwnTask()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).SendCommentEmailAsync(MemberId, MemberId, "Ship it", "note to self");

            _email.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendCommentEmailAsync_SaysNothingWhenTheTaskHasNoAssignee()
        {
            await SeedAsync();

            await using var act = NewContext();
            await NewSut(act).SendCommentEmailAsync(Guid.Empty, LeadId, "Ship it", "anyone there");

            _email.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task SendCommentEmailAsync_LetsAFailedSendEscape()
        {
            await SeedAsync();
            TheMailProviderIsDown();

            await using var act = NewContext();
            await Assert.ThrowsAsync<EmailSendException>(
                () => NewSut(act).SendCommentEmailAsync(MemberId, LeadId, "Ship it", "looks good"));
        }

        /// <summary>
        /// The Try prefix is the contract. The controllers call these after the mutation has
        /// committed, so a mail failure is logged and swallowed. The Verify proves the send was
        /// really attempted, so an empty Try method cannot pass.
        /// </summary>
        [Fact]
        public async Task TrySendTaskAssignmentEmailAsync_AttemptsTheSendAndSwallowsItsFailure()
        {
            await SeedAsync();
            TheMailProviderIsDown();

            await using var act = NewContext();
            var thrown = await Record.ExceptionAsync(
                () => NewSut(act).TrySendTaskAssignmentEmailAsync(MemberId, "Ship it", "Dev Team", "lead"));

            Assert.Null(thrown);
            _email.Verify(e => e.SendTaskAssignmentEmailAsync(
                "member@example.com", "member", "Ship it", "Dev Team", "lead"), Times.Once);
        }

        [Fact]
        public async Task TrySendCommentEmailAsync_AttemptsTheSendAndSwallowsItsFailure()
        {
            await SeedAsync();
            TheMailProviderIsDown();

            await using var act = NewContext();
            var thrown = await Record.ExceptionAsync(
                () => NewSut(act).TrySendCommentEmailAsync(MemberId, LeadId, "Ship it", "looks good"));

            Assert.Null(thrown);
            _email.Verify(e => e.SendTaskCommentEmailAsync(
                "member@example.com", "member", "lead", "Ship it", "looks good"), Times.Once);
        }
    }
}
