using Auth.Models.DTOs.Email;
using Auth.Services.Interfaces.Email;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace Auth.Services.Services.Email.Providers
{
    /// <summary>
    /// Shared transport for every SMTP-based provider. Plain SMTP and GMass differ only
    /// in credentials and host, so the wire logic lives here once — subclasses just
    /// declare their connection details.
    /// </summary>
    public abstract class SmtpProviderBase : IEmailProvider
    {
        private readonly ILogger _logger;

        protected SmtpProviderBase(ILogger logger) => _logger = logger;

        public abstract string Key { get; }
        public abstract string DisplayName { get; }
        public abstract bool IsConfigured { get; }
        public abstract string? ConfigurationHint { get; }

        protected abstract string Host { get; }
        protected abstract int Port { get; }
        protected abstract string? Username { get; }
        protected abstract string? Password { get; }
        protected abstract bool EnableSsl { get; }
        protected abstract string? DefaultFromEmail { get; }
        protected abstract string? DefaultFromName { get; }

        /// <summary>
        /// Splits an operator-entered CC field.
        ///
        /// Accepts commas and semicolons because the team's spreadsheet uses both, sometimes in
        /// the same cell. Blank entries are dropped rather than passed on as empty addresses.
        /// </summary>
        protected static IEnumerable<string> SplitAddresses(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) yield break;

            foreach (var part in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) yield return trimmed;
            }
        }

        public async Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                return EmailSendResult.Fail(Key, ConfigurationHint ?? "Provider is not configured.", isTransient: false);

            var fromEmail = email.FromEmail ?? DefaultFromEmail;
            if (string.IsNullOrWhiteSpace(fromEmail))
                return EmailSendResult.Fail(Key, "No from-address configured.", isTransient: false);

            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(fromEmail, email.FromName ?? DefaultFromName ?? fromEmail),
                    Subject = email.Subject,
                    Body = email.HtmlBody,
                    IsBodyHtml = true
                };

                message.To.Add(string.IsNullOrWhiteSpace(email.ToName)
                    ? new MailAddress(email.ToEmail)
                    : new MailAddress(email.ToEmail, email.ToName));

                // A malformed CC must not cost us the email. The address is operator-entered
                // free text from a spreadsheet, so one bad entry among hundreds is expected —
                // and the recipient still wants the message even if the copy cannot be sent.
                foreach (var cc in SplitAddresses(email.Cc))
                {
                    try
                    {
                        message.CC.Add(new MailAddress(cc));
                    }
                    catch (FormatException)
                    {
                        _logger.LogWarning("[{Key}] Skipped malformed CC address '{Cc}'.", Key, cc);
                    }
                }

                if (!string.IsNullOrWhiteSpace(email.TextBody))
                {
                    // Multipart/alternative: clients that can't render HTML still get readable text,
                    // and spam filters score a text part favourably.
                    message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                        email.TextBody, null, "text/plain"));
                    message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                        email.HtmlBody, null, "text/html"));
                }

                if (!string.IsNullOrWhiteSpace(email.ReplyTo))
                    message.ReplyToList.Add(new MailAddress(email.ReplyTo));

                using var client = new SmtpClient(Host, Port) { EnableSsl = EnableSsl };

                if (!string.IsNullOrWhiteSpace(Username))
                    client.Credentials = new NetworkCredential(Username, Password);

                await client.SendMailAsync(message, cancellationToken);

                _logger.LogInformation("[{Provider}] Email sent to {Email} (tag={Tag})", Key, email.ToEmail, email.Tag);
                return EmailSendResult.Ok(Key);
            }
            catch (SmtpFailedRecipientException ex)
            {
                // The address itself is bad — retrying on another provider would fail identically.
                _logger.LogWarning(ex, "[{Provider}] Rejected recipient {Email}", Key, email.ToEmail);
                return EmailSendResult.Fail(Key, $"Recipient rejected: {ex.Message}", isTransient: false);
            }
            catch (FormatException ex)
            {
                return EmailSendResult.Fail(Key, $"Malformed address: {ex.Message}", isTransient: false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{Provider}] Failed to send to {Email}", Key, email.ToEmail);
                return EmailSendResult.Fail(Key, ex.Message);
            }
        }
    }
}
