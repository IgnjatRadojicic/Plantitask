using Moq;
using Plantitask.Core.Common;
using Plantitask.Core.Events;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Events.Handlers;
using static Plantitask.Tests.Helpers.TestIds;

namespace Plantitask.Tests.Events
{
    public class EmailEventHandlerTests
    {
        private readonly Mock<ITaskEmailSender> _emails = new();
        private readonly Mock<IEmailService> _emailService = new();

        private EmailEventHandler NewSut() => new(_emails.Object, _emailService.Object);

        private static EventContext By(Guid actorId, string userName = "lead") => new()
        {
            OccurredAt = DateTime.UtcNow,
            Source = EventSource.Request,
            Actor = new EventActor(actorId, userName, $"{userName}@test.com"),
        };

        private static EventContext ByNobody() => new()
        {
            OccurredAt = DateTime.UtcNow,
            Source = EventSource.Job,
            Actor = null,
        };

        private static TaskCreated Created(Guid? assigneeId) => new()
        {
            TaskId = TaskId, GroupId = GroupId, Title = "Ship it", StatusId = 1,
            AssigneeId = assigneeId, GroupName = "Dev Team"
        };

        private static TaskAssigned Assigned(Guid assigneeId) => new()
        {
            TaskId = TaskId, GroupId = GroupId, Title = "Ship it",
            AssigneeId = assigneeId, AssigneeName = "member", GroupName = "Dev Team"
        };

        private static UserRegistered Registered() => new()
        {
            UserId = MemberId, UserName = "member", Email = "member@example.com",
            DisplayName = "Ada", IpAddress = "1.1.1.1", UserAgent = null
        };

        private static CommentAdded Commented(Guid? taskAssigneeId) => new()
        {
            CommentId = Guid.NewGuid(), TaskId = TaskId, GroupId = GroupId, Content = "looks good",
            TaskTitle = "Ship it", TaskAssigneeId = taskAssigneeId, TaskCreatedById = LeadId
        };

        [Fact]
        public async Task Created_WithAnAssignee_EmailsThemNamingTheActor()
        {
            await NewSut().HandleAsync(Created(MemberId), By(LeadId), CancellationToken.None);

            _emails.Verify(s => s.SendTaskAssignmentEmailAsync(MemberId, "Ship it", "Dev Team", "lead"), Times.Once);
            _emails.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Created_WithoutAnAssignee_SendsNothing()
        {
            await NewSut().HandleAsync(Created(null), By(LeadId), CancellationToken.None);

            _emails.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Created_AssignedToYourself_SendsNothing()
        {
            await NewSut().HandleAsync(Created(LeadId), By(LeadId), CancellationToken.None);

            _emails.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The name in the email is whoever did the assigning, taken from the event context. It
        /// is not the task's creator, who may be someone else entirely.
        /// </summary>
        [Fact]
        public async Task Assigned_EmailsTheAssigneeNamingTheActor()
        {
            await NewSut().HandleAsync(Assigned(MemberId), By(OtherLeadId, "otherlead"), CancellationToken.None);

            _emails.Verify(s => s.SendTaskAssignmentEmailAsync(MemberId, "Ship it", "Dev Team", "otherlead"), Times.Once);
            _emails.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Assigned_ToYourself_SendsNothing()
        {
            await NewSut().HandleAsync(Assigned(LeadId), By(LeadId), CancellationToken.None);

            _emails.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Assigned_WithNoActor_SendsNothing()
        {
            await NewSut().HandleAsync(Assigned(MemberId), ByNobody(), CancellationToken.None);

            _emails.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CommentAdded_PassesTheAssigneeAndTheCommenterToTheSender()
        {
            await NewSut().HandleAsync(Commented(MemberId), By(LeadId), CancellationToken.None);

            _emails.Verify(s => s.SendCommentEmailAsync(MemberId, LeadId, "Ship it", "looks good"), Times.Once);
            _emails.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Guid.Empty is how the sender is told there is no assignee. Whether that sends is the
        /// sender's rule and is tested there.
        /// </summary>
        [Fact]
        public async Task CommentAdded_OnAnUnassignedTask_PassesAnEmptyAssignee()
        {
            await NewSut().HandleAsync(Commented(null), By(LeadId), CancellationToken.None);

            _emails.Verify(s => s.SendCommentEmailAsync(Guid.Empty, LeadId, "Ship it", "looks good"), Times.Once);
            _emails.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task CommentAdded_WithNoActor_SendsNothing()
        {
            await NewSut().HandleAsync(Commented(MemberId), ByNobody(), CancellationToken.None);

            _emails.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Registration happens before a token exists, so there is no actor. The email goes to
        /// the address in the event and greets by the name captured when the account was made.
        /// </summary>
        [Fact]
        public async Task Registered_SendsTheWelcomeEmailWithNoActor()
        {
            await NewSut().HandleAsync(Registered(), ByNobody(), CancellationToken.None);

            _emailService.Verify(s => s.SendWelcomeEmailAsync("member@example.com", "Ada"), Times.Once);
            _emailService.VerifyNoOtherCalls();
            _emails.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Registered_AFailedSendEscapesTheHandler()
        {
            _emailService
                .Setup(s => s.SendWelcomeEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new EmailSendException("provider down"));

            await Assert.ThrowsAsync<EmailSendException>(
                () => NewSut().HandleAsync(Registered(), ByNobody(), CancellationToken.None));
        }

        /// <summary>
        /// The drainer writes the inbox row only when the handler returns. A swallowed mail
        /// failure would mark the email done and it would never be retried.
        /// </summary>
        [Fact]
        public async Task AFailedSend_EscapesTheHandler()
        {
            _emails
                .Setup(s => s.SendTaskAssignmentEmailAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new EmailSendException("provider down"));

            await Assert.ThrowsAsync<EmailSendException>(
                () => NewSut().HandleAsync(Assigned(MemberId), By(LeadId), CancellationToken.None));
        }

        /// <summary>
        /// The name is the inbox key. Renaming it makes every message already handled look
        /// unhandled, and its email would be sent again.
        /// </summary>
        [Fact]
        public void Name_IsEmail()
        {
            Assert.Equal("email", NewSut().Name);
        }
    }
}
