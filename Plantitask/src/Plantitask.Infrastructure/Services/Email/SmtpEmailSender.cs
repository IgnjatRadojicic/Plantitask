using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Plantitask.Core.Common;
using Plantitask.Core.Interfaces;
using Plantitask.Core.Models;

namespace Plantitask.Infrastructure.Services.Email
{
    /// <summary>
    /// Sends over plain SMTP via MailKit - the local-dev and self-hosted alternative to
    /// SendGrid. Same exception contract: failures become EmailSendException.
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        private readonly EmailSettings _emailSettings;
        private readonly SmtpSettings _smtpSettings;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(
            IOptions<EmailSettings> emailSettings,
            IOptions<SmtpSettings> smtpSettings,
            ILogger<SmtpEmailSender> logger)
        {
            _emailSettings = emailSettings.Value;
            _smtpSettings = smtpSettings.Value;
            _logger = logger;
        }

        /// <summary>
        /// Connects, authenticates, sends and disconnects per message. Cancellation passes
        /// through untouched; every other failure is wrapped in EmailSendException with the
        /// host named for the logs.
        /// </summary>
        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            var mimeMessage = new MimeMessage();
            mimeMessage.From.Add(new MailboxAddress(_emailSettings.FromName, _emailSettings.FromEmail));
            mimeMessage.To.Add(MailboxAddress.Parse(message.ToEmail));
            mimeMessage.Subject = message.Subject;
            mimeMessage.Body = new BodyBuilder { HtmlBody = message.HtmlContent }.ToMessageBody();

            var socketOptions = _smtpSettings.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.SslOnConnect;

            try
            {
                using var client = new SmtpClient();

                await client.ConnectAsync(_smtpSettings.Host, _smtpSettings.Port, socketOptions, cancellationToken);
                await client.AuthenticateAsync(_smtpSettings.UserName, _smtpSettings.Password, cancellationToken);
                await client.SendAsync(mimeMessage, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to send {EmailType} email to {Email} via SMTP host {Host}",
                    message.EmailType, message.ToEmail, _smtpSettings.Host);

                throw new EmailSendException($"SMTP host {_smtpSettings.Host} rejected the {message.EmailType} email", ex);
            }

            _logger.LogInformation("Sent {EmailType} email to {Email} via SMTP", message.EmailType, message.ToEmail);
        }
    }
}
