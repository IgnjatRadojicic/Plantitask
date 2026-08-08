using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Models;
using Plantitask.Infrastructure.Services.Email;

namespace Plantitask.Infrastructure.Services
{
    /// <summary>
    /// Composes every outbound email from its template and hands it to whichever IEmailSender
    /// is registered (SendGrid in production, SMTP locally). No sending logic lives here -
    /// this class only knows subjects and which template goes with which occasion.
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly IEmailSender _sender;

        public EmailService(IEmailSender sender)
        {
            _sender = sender;
        }

        /// <summary>Welcome mail on account creation, greeting by first name or username.</summary>
        public Task SendWelcomeEmailAsync(string email, string displayName)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                $"Welcome to Plantitask, {displayName}!",
                EmailTemplates.Welcome(displayName),
                "welcome"));
        }

        /// <summary>The reset link mail - the only place the plaintext reset token ever appears.</summary>
        public Task SendPasswordResetEmailAsync(string email, string userName, string resetLink)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                "Reset Your Password - Plantitask",
                EmailTemplates.PasswordReset(userName, resetLink),
                "password reset"));
        }

        /// <summary>Assignment notice with the task title in the subject line.</summary>
        public Task SendTaskAssignmentEmailAsync(string email, string userName, string taskTitle, string groupName, string assignedBy)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                $"New Task Assigned: {taskTitle}",
                EmailTemplates.TaskAssignment(userName, taskTitle, groupName, assignedBy),
                "task assignment"));
        }

        /// <summary>Invitation mail carrying the join code. Wired up but not yet called anywhere - Wave 0 feature.</summary>
        public Task SendGroupInvitationEmailAsync(string email, string inviterName, string groupName, string groupCode)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                $"You've been invited to join {groupName}",
                EmailTemplates.GroupInvitation(inviterName, groupName, groupCode),
                "group invitation"));
        }

        /// <summary>New-comment notice for the task's assignee.</summary>
        public Task SendTaskCommentEmailAsync(string email, string userName, string commenterName, string taskTitle, string commentText)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                $"New comment on {taskTitle}",
                EmailTemplates.TaskComment(userName, commenterName, taskTitle, commentText),
                "task comment"));
        }

        /// <summary>The scheduled due-soon reminder mail.</summary>
        public Task SendTaskDueSoonEmailAsync(string email, string userName, string taskTitle, DateTime dueDate)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                $"Task Due Soon: {taskTitle}",
                EmailTemplates.TaskDueSoon(userName, taskTitle, dueDate),
                "task due soon"));
        }

        /// <summary>The daily overdue digest, with the count in the subject so the inbox row already tells the story.</summary>
        public Task SendTaskOverdueDigestEmailAsync(string email, string userName, int overdueCount, IReadOnlyList<OverdueTaskLine> worstTasks)
        {
            var subject = overdueCount == 1
                ? "You have 1 overdue task"
                : $"You have {overdueCount} overdue tasks";

            return _sender.SendAsync(new EmailMessage(
                email,
                subject,
                EmailTemplates.TaskOverdueDigest(userName, overdueCount, worstTasks),
                "task overdue digest"));
        }

        /// <summary>The six-digit code mail, code in the subject so it shows in previews without opening.</summary>
        public Task SendEmailVerificationCodeAsync(string email, string userName, string code)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                $"Your Plantitask verification code: {code}",
                EmailTemplates.EmailVerification(userName, code),
                "email verification"));
        }
    }
}
