using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using Plantitask.Core.Common;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Models;

namespace Plantitask.Infrastructure.Services.Email
{
    public class SendGridEmailSender : IEmailSender
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<SendGridEmailSender> _logger;
        private readonly SendGridClient _client;

        public SendGridEmailSender(IOptions<EmailSettings> settings, ILogger<SendGridEmailSender> logger)
        {
            _settings = settings.Value;
            _logger = logger;
            _client = new SendGridClient(_settings.SendGridApiKey);
        }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            var from = new EmailAddress(_settings.FromEmail, _settings.FromName);
            var to = new EmailAddress(message.ToEmail);
            var msg = MailHelper.CreateSingleEmail(from, to, message.Subject, null, message.HtmlContent);

            var response = await _client.SendEmailAsync(msg, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Body.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to send {EmailType} email to {Email}. Status: {StatusCode}. Body: {Body}",
                    message.EmailType, message.ToEmail, response.StatusCode, body);

                throw new EmailSendException($"SendGrid rejected the {message.EmailType} email with status {response.StatusCode}");
            }

            _logger.LogInformation("Sent {EmailType} email to {Email} via SendGrid", message.EmailType, message.ToEmail);
        }
    }
}
