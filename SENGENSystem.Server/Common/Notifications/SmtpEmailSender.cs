using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace SENGENSystem.Server.Common.Notifications
{
    /// <summary>
    /// Sends email over SMTP (Gmail by default). When no credentials are configured it degrades to
    /// logging the message, so the app runs in environments without mail secrets. Delivery failures
    /// are caught and returned as a non-sent <see cref="EmailResult"/> rather than thrown, so a
    /// registration is never lost just because its confirmation email bounced.
    /// </summary>
    public sealed class SmtpEmailSender : IEmailSender
    {
        private readonly EmailOptions _options;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task<EmailResult> SendAsync(
            string toEmail, string toName, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            if (!_options.IsConfigured)
            {
                _logger.LogWarning(
                    "Email not configured; skipping send of \"{Subject}\" to {ToEmail}.", subject, toEmail);
                return EmailResult.Failed("Email is not configured (no SMTP credentials).");
            }

            var fromAddress = string.IsNullOrWhiteSpace(_options.FromAddress) ? _options.User : _options.FromAddress;

            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(fromAddress, _options.FromName),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };
                message.To.Add(new MailAddress(toEmail, toName));

                using var client = new SmtpClient(_options.Host, _options.Port)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(_options.User, _options.Password)
                };

                await client.SendMailAsync(message, cancellationToken);
                _logger.LogInformation("Sent email \"{Subject}\" to {ToEmail}.", subject, toEmail);
                return EmailResult.Ok($"Sent to {toEmail}.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email \"{Subject}\" to {ToEmail}.", subject, toEmail);
                return EmailResult.Failed($"Delivery failed: {ex.Message}");
            }
        }
    }
}
