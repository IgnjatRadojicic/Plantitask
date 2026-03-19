using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using Plantitask.Core.Common;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services.Email;

namespace Plantitask.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;
        private readonly SendGridClient _client;

        public EmailService(IOptions<EmailSettings> settings, ILogger<EmailService> logger)
        {
            _settings = settings.Value;
            _logger = logger;
            _client = new SendGridClient(_settings.SendGridApiKey);
        }

        public async Task SendWelcomeEmailAsync(string email, string firstName)
        {
            var html = EmailTemplates.Welcome(firstName);
            await SendEmailAsync(email, $"Welcome to Plantitask, {firstName}!", html);
        }

        public async Task SendPasswordResetEmailAsync(string email, string userName, string resetLink)
        {
            var html = EmailTemplates.PasswordReset(userName, resetLink);
            await SendEmailAsync(email, "Reset Your Password - Plantitask", html);
        }

        public async Task SendTaskAssignmentEmailAsync(string email, string userName, string taskTitle, string groupName, string assignedBy)
        {
            var html = EmailTemplates.TaskAssignment(userName, taskTitle, groupName, assignedBy);
            await SendEmailAsync(email, $"New Task Assigned: {taskTitle}", html);
        }

        public async Task SendGroupInvitationEmailAsync(string email, string inviterName, string groupName, string groupCode)
        {
            var html = EmailTemplates.GroupInvitation(inviterName, groupName, groupCode);
            await SendEmailAsync(email, $"You've been invited to join {groupName}", html);
        }

        public async Task SendTaskCommentEmailAsync(string email, string userName, string commenterName, string taskTitle, string commentText)
        {
            var html = EmailTemplates.TaskComment(userName, commenterName, taskTitle, commentText);
            await SendEmailAsync(email, $"New comment on {taskTitle}", html);
        }

        public async Task SendTaskDueSoonEmailAsync(string email, string userName, string taskTitle, DateTime dueDate)
        {
            var html = EmailTemplates.TaskDueSoon(userName, taskTitle, dueDate);
            await SendEmailAsync(email, $"Task Due Soon: {taskTitle}", html);
        }

        public async Task SendTaskOverdueEmailAsync(string email, string userName, string taskTitle, int daysOverdue)
        {
            var html = EmailTemplates.TaskOverdue(userName, taskTitle, daysOverdue);
            await SendEmailAsync(email, $"Task Overdue: {taskTitle}", html);
        }

        public async Task SendEmailVerificationCodeAsync(string email, string userName, string code)
        {
            var html = EmailTemplates.EmailVerification(userName, code);
            await SendEmailAsync(email, $"Your Plantitask verification code: {code}", html);
        }

        private async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
        {
            var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
            var to = new EmailAddress(toEmail);
            var msg = MailHelper.CreateSingleEmail(from, to, subject, null, htmlContent);

            var response = await _client.SendEmailAsync(msg);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
            }
            else
            {
                var body = await response.Body.ReadAsStringAsync();
                _logger.LogError("Failed to send email to {Email}. Status: {StatusCode}. Body: {Body}",
                    toEmail, response.StatusCode, body);
            }
        }
    }
}