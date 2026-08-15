using Plantitask.Core.DTO.Tasks;
using Plantitask.Infrastructure.Services.Email;

namespace Plantitask.Tests.Services
{
    public class EmailTemplatesTests
    {
        private const string Payload = "<script>alert('xss')</script>";

        private static string Render(string template) => template switch
        {
            "Welcome" => EmailTemplates.Welcome(Payload),
            "PasswordReset" => EmailTemplates.PasswordReset(Payload, Payload),
            "TaskAssignment" => EmailTemplates.TaskAssignment(Payload, Payload, Payload, Payload),
            "GroupInvitation" => EmailTemplates.GroupInvitation(Payload, Payload, Payload),
            "TaskComment" => EmailTemplates.TaskComment(Payload, Payload, Payload, Payload),
            "TaskDueSoon" => EmailTemplates.TaskDueSoon(Payload, Payload, DateTime.UtcNow),
            "EmailVerification" => EmailTemplates.EmailVerification(Payload, Payload),
            "TaskOverdueDigest" => EmailTemplates.TaskOverdueDigest(
                Payload, 3, [new OverdueTaskLine(Payload, 2)]),
            _ => throw new ArgumentOutOfRangeException(nameof(template), template, null)
        };

        /// <summary>
        /// The one rule this file exists to enforce. Every user controlled string reaching the
        /// markup is encoded, so a display name or a comment cannot carry markup into someone
        /// else's inbox. Each case fills every user supplied slot of its template with the same
        /// payload so no slot can be the one that was forgotten.
        /// </summary>
        [Theory]
        [InlineData("Welcome")]
        [InlineData("PasswordReset")]
        [InlineData("TaskAssignment")]
        [InlineData("GroupInvitation")]
        [InlineData("TaskComment")]
        [InlineData("TaskDueSoon")]
        [InlineData("EmailVerification")]
        [InlineData("TaskOverdueDigest")]
        public void EveryTemplateEncodesTheTextItIsGiven(string template)
        {
            var html = Render(template);

            Assert.DoesNotContain("<script>", html);
            Assert.DoesNotContain("</script>", html);
            Assert.Contains("&lt;script&gt;", html);
        }

        [Theory]
        [InlineData("Welcome")]
        [InlineData("PasswordReset")]
        [InlineData("TaskAssignment")]
        [InlineData("GroupInvitation")]
        [InlineData("TaskComment")]
        [InlineData("TaskDueSoon")]
        [InlineData("EmailVerification")]
        [InlineData("TaskOverdueDigest")]
        public void EveryTemplateProducesACompleteDocument(string template)
        {
            var html = Render(template);

            Assert.Contains("<!DOCTYPE html>", html);
            Assert.Contains("</html>", html);
        }

        [Fact]
        public void Welcome_GreetsTheDisplayName()
        {
            Assert.Contains("Welcome to Plantitask, Ada", EmailTemplates.Welcome("Ada"));
        }

        [Fact]
        public void PasswordReset_PutsTheLinkInTheAnchorHref()
        {
            var html = EmailTemplates.PasswordReset("ada", "https://example.com/reset?token=abc");

            Assert.Contains("href='https://example.com/reset?token=abc'", html);
        }

        [Fact]
        public void GroupInvitation_ShowsTheJoinCode()
        {
            var html = EmailTemplates.GroupInvitation("lead", "Dev Team", "DEV12345");

            Assert.Contains("DEV12345", html);
            Assert.Contains("Dev Team", html);
        }

        [Fact]
        public void EmailVerification_ShowsTheCode()
        {
            Assert.Contains("483920", EmailTemplates.EmailVerification("ada", "483920"));
        }

        [Fact]
        public void TaskDueSoon_FormatsTheDueDateAsReadableUtc()
        {
            var html = EmailTemplates.TaskDueSoon(
                "ada", "Ship the release", new DateTime(2026, 9, 1, 14, 30, 0, DateTimeKind.Utc));

            Assert.Contains("September 01, 2026", html);
            Assert.Contains("2:30 PM", html);
            Assert.Contains("UTC", html);
        }

        [Fact]
        public void TaskOverdueDigest_UsesTheSingularNounForExactlyOne()
        {
            var html = EmailTemplates.TaskOverdueDigest("ada", 1, [new OverdueTaskLine("Ship it", 3)]);

            Assert.Contains("overdue task:", html);
            Assert.DoesNotContain("overdue tasks:", html);
        }

        [Fact]
        public void TaskOverdueDigest_UsesThePluralNounForAnythingElse()
        {
            var html = EmailTemplates.TaskOverdueDigest(
                "ada", 2, [new OverdueTaskLine("Ship it", 3), new OverdueTaskLine("Write docs", 1)]);

            Assert.Contains("overdue tasks:", html);
        }

        /// <summary>
        /// The caller caps how many tasks are listed so the email stays short. The remainder line
        /// is what stops the digest quietly under reporting when the backlog is worse than the
        /// list shown.
        /// </summary>
        [Fact]
        public void TaskOverdueDigest_SaysHowManyMoreWereNotListed()
        {
            var html = EmailTemplates.TaskOverdueDigest(
                "ada", 10, [new OverdueTaskLine("Ship it", 3), new OverdueTaskLine("Write docs", 1)]);

            Assert.Contains("and 8 more.", html);
        }

        [Fact]
        public void TaskOverdueDigest_OmitsTheRemainderLineWhenEverythingIsListed()
        {
            var html = EmailTemplates.TaskOverdueDigest(
                "ada", 2, [new OverdueTaskLine("Ship it", 3), new OverdueTaskLine("Write docs", 1)]);

            Assert.DoesNotContain("more.", html);
        }

        [Theory]
        [InlineData(0, "overdue today")]
        [InlineData(1, "1 day overdue")]
        [InlineData(2, "2 days overdue")]
        [InlineData(45, "45 days overdue")]
        public void TaskOverdueDigest_PhrasesTheAgeOfEachLine(int daysOverdue, string expected)
        {
            var html = EmailTemplates.TaskOverdueDigest(
                "ada", 1, [new OverdueTaskLine("Ship it", daysOverdue)]);

            Assert.Contains(expected, html);
        }

        [Fact]
        public void TaskOverdueDigest_ListsEveryTaskItWasGiven()
        {
            var html = EmailTemplates.TaskOverdueDigest("ada", 3,
            [
                new OverdueTaskLine("First task", 1),
                new OverdueTaskLine("Second task", 2),
                new OverdueTaskLine("Third task", 3)
            ]);

            Assert.Contains("First task", html);
            Assert.Contains("Second task", html);
            Assert.Contains("Third task", html);
        }

        [Fact]
        public void TaskOverdueDigest_HandlesAnEmptyListWithoutBreaking()
        {
            var html = EmailTemplates.TaskOverdueDigest("ada", 0, []);

            Assert.Contains("</html>", html);
            Assert.DoesNotContain("more.", html);
        }
    }
}
