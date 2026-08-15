using Moq;
using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Models;
using Plantitask.Infrastructure.Services;

namespace Plantitask.Tests.Services
{
    /// <summary>
    /// EmailService composes a message and hands it to whichever sender is registered. The
    /// sender is the thing that talks to the network, so it is mocked and the assertions are
    /// about what was handed over.
    /// </summary>
    public class EmailServiceTests
    {
        private readonly Mock<IEmailSender> _sender = new();
        private readonly List<EmailMessage> _sent = [];
        private readonly EmailService _sut;

        public EmailServiceTests()
        {
            _sender
                .Setup(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                .Callback<EmailMessage, CancellationToken>((message, _) => _sent.Add(message))
                .Returns(Task.CompletedTask);

            _sut = new EmailService(_sender.Object);
        }

        private EmailMessage Sent => Assert.Single(_sent);

        [Fact]
        public async Task SendWelcomeEmailAsync_GreetsByTheDisplayName()
        {
            await _sut.SendWelcomeEmailAsync("new@example.com", "Ada");

            Assert.Equal("new@example.com", Sent.ToEmail);
            Assert.Equal("Welcome to Plantitask, Ada!", Sent.Subject);
            Assert.Equal("welcome", Sent.EmailType);
            Assert.Contains("Ada", Sent.HtmlContent);
        }

        [Fact]
        public async Task SendPasswordResetEmailAsync_CarriesTheLinkInTheBodyAndNotTheSubject()
        {
            var link = "https://plantitask.example.com/reset?token=abc123";

            await _sut.SendPasswordResetEmailAsync("user@example.com", "ada", link);

            Assert.Equal("Reset Your Password - Plantitask", Sent.Subject);
            Assert.DoesNotContain("abc123", Sent.Subject);
            Assert.Contains(link, Sent.HtmlContent);
            Assert.Equal("password reset", Sent.EmailType);
        }

        [Fact]
        public async Task SendTaskAssignmentEmailAsync_PutsTheTaskTitleInTheSubject()
        {
            await _sut.SendTaskAssignmentEmailAsync(
                "user@example.com", "ada", "Ship the release", "Dev Team", "lead");

            Assert.Equal("New Task Assigned: Ship the release", Sent.Subject);
            Assert.Equal("task assignment", Sent.EmailType);
            Assert.Contains("Dev Team", Sent.HtmlContent);
            Assert.Contains("lead", Sent.HtmlContent);
        }

        [Fact]
        public async Task SendGroupInvitationEmailAsync_NamesTheGroupAndCarriesTheJoinCode()
        {
            await _sut.SendGroupInvitationEmailAsync(
                "invitee@example.com", "lead", "Dev Team", "DEV12345");

            Assert.Equal("You've been invited to join Dev Team", Sent.Subject);
            Assert.Contains("DEV12345", Sent.HtmlContent);
            Assert.Equal("group invitation", Sent.EmailType);
        }

        [Fact]
        public async Task SendTaskCommentEmailAsync_NamesTheTaskInTheSubject()
        {
            await _sut.SendTaskCommentEmailAsync(
                "user@example.com", "ada", "lead", "Ship the release", "looks good to me");

            Assert.Equal("New comment on Ship the release", Sent.Subject);
            Assert.Contains("looks good to me", Sent.HtmlContent);
            Assert.Equal("task comment", Sent.EmailType);
        }

        [Fact]
        public async Task SendTaskDueSoonEmailAsync_NamesTheTaskInTheSubject()
        {
            await _sut.SendTaskDueSoonEmailAsync(
                "user@example.com", "ada", "Ship the release", new DateTime(2026, 9, 1, 14, 30, 0, DateTimeKind.Utc));

            Assert.Equal("Task Due Soon: Ship the release", Sent.Subject);
            Assert.Equal("task due soon", Sent.EmailType);
        }

        /// <summary>
        /// The verification code goes in the subject on purpose so it shows in a notification
        /// preview without the recipient having to open anything.
        /// </summary>
        [Fact]
        public async Task SendEmailVerificationCodeAsync_PutsTheCodeInTheSubject()
        {
            await _sut.SendEmailVerificationCodeAsync("user@example.com", "ada", "483920");

            Assert.Equal("Your Plantitask verification code: 483920", Sent.Subject);
            Assert.Contains("483920", Sent.HtmlContent);
            Assert.Equal("email verification", Sent.EmailType);
        }

        /// <summary>
        /// The digest subject is the only one with a branch in it. The count carries the whole
        /// message so the inbox row is useful before anything is opened.
        /// </summary>
        [Fact]
        public async Task SendTaskOverdueDigestEmailAsync_UsesTheSingularSubjectForExactlyOne()
        {
            await _sut.SendTaskOverdueDigestEmailAsync(
                "user@example.com", "ada", 1, [new OverdueTaskLine("Ship the release", 2)]);

            Assert.Equal("You have 1 overdue task", Sent.Subject);
            Assert.Equal("task overdue digest", Sent.EmailType);
        }

        [Fact]
        public async Task SendTaskOverdueDigestEmailAsync_UsesThePluralSubjectForAnythingElse()
        {
            await _sut.SendTaskOverdueDigestEmailAsync(
                "user@example.com", "ada", 4,
                [new OverdueTaskLine("Ship the release", 2), new OverdueTaskLine("Write the docs", 5)]);

            Assert.Equal("You have 4 overdue tasks", Sent.Subject);
        }

        /// <summary>
        /// Every message is tagged with what kind of email it was, and the senders log that tag
        /// when a send fails. A duplicated or missing tag would make a delivery failure hard to
        /// trace back to the code that triggered it.
        /// </summary>
        [Fact]
        public async Task EveryKindOfEmailCarriesItsOwnTypeTag()
        {
            await _sut.SendWelcomeEmailAsync("a@example.com", "ada");
            await _sut.SendPasswordResetEmailAsync("a@example.com", "ada", "https://example.com");
            await _sut.SendTaskAssignmentEmailAsync("a@example.com", "ada", "t", "g", "lead");
            await _sut.SendGroupInvitationEmailAsync("a@example.com", "lead", "g", "CODE1234");
            await _sut.SendTaskCommentEmailAsync("a@example.com", "ada", "lead", "t", "c");
            await _sut.SendTaskDueSoonEmailAsync("a@example.com", "ada", "t", DateTime.UtcNow);
            await _sut.SendEmailVerificationCodeAsync("a@example.com", "ada", "123456");
            await _sut.SendTaskOverdueDigestEmailAsync("a@example.com", "ada", 1, [new OverdueTaskLine("t", 1)]);

            var tags = _sent.Select(m => m.EmailType).ToList();

            Assert.Equal(8, tags.Count);
            Assert.Equal(tags.Count, tags.Distinct().Count());
            Assert.All(tags, tag => Assert.False(string.IsNullOrWhiteSpace(tag)));
        }

        [Fact]
        public async Task EveryKindOfEmailProducesAFullHtmlDocument()
        {
            await _sut.SendWelcomeEmailAsync("a@example.com", "ada");
            await _sut.SendTaskOverdueDigestEmailAsync("a@example.com", "ada", 1, [new OverdueTaskLine("t", 1)]);

            Assert.All(_sent, m =>
            {
                Assert.Contains("<!DOCTYPE html>", m.HtmlContent);
                Assert.Contains("</html>", m.HtmlContent);
            });
        }

        /// <summary>
        /// Nothing here catches a send failure. A rejected email surfaces to the caller so it can
        /// decide whether that particular message was best effort or load bearing.
        /// </summary>
        [Fact]
        public async Task ASendFailureIsNotSwallowed()
        {
            _sender
                .Setup(s => s.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Plantitask.Core.Common.EmailSendException("provider rejected it"));

            await Assert.ThrowsAsync<Plantitask.Core.Common.EmailSendException>(
                () => _sut.SendWelcomeEmailAsync("a@example.com", "ada"));
        }
    }
}
