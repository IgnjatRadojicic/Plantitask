using Plantitask.Core.DTO.Tasks;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Models;
using Plantitask.Infrastructure.Services.Email;

namespace Plantitask.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly IEmailSender _sender;

        public EmailService(IEmailSender sender)
        {
            _sender = sender;
        }

        public Task SendWelcomeEmailAsync(string email, string displayName)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                $"Welcome to Plantitask, {displayName}!",
                EmailTemplates.Welcome(displayName),
                "welcome"));
        }

        public Task SendPasswordResetEmailAsync(string email, string userName, string resetLink)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                "Reset Your Password - Plantitask",
                EmailTemplates.PasswordReset(userName, resetLink),
                "password reset"));
        }

        public Task SendTaskAssignmentEmailAsync(string email, string userName, string taskTitle, string groupName, string assignedBy)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                $"New Task Assigned: {taskTitle}",
                EmailTemplates.TaskAssignment(userName, taskTitle, groupName, assignedBy),
                "task assignment"));
        }

        public Task SendGroupInvitationEmailAsync(string email, string inviterName, string groupName, string groupCode)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                $"You've been invited to join {groupName}",
                EmailTemplates.GroupInvitation(inviterName, groupName, groupCode),
                "group invitation"));
        }

        public Task SendTaskCommentEmailAsync(string email, string userName, string commenterName, string taskTitle, string commentText)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                $"New comment on {taskTitle}",
                EmailTemplates.TaskComment(userName, commenterName, taskTitle, commentText),
                "task comment"));
        }

        public Task SendTaskDueSoonEmailAsync(string email, string userName, string taskTitle, DateTime dueDate)
        {
            return _sender.SendAsync(new EmailMessage(
                email,
                $"Task Due Soon: {taskTitle}",
                EmailTemplates.TaskDueSoon(userName, taskTitle, dueDate),
                "task due soon"));
        }

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
