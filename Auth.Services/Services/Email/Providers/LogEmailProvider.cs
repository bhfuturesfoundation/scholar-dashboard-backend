using Auth.Models.DTOs.Email;
using Auth.Services.Interfaces.Email;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Email.Providers
{
    /// <summary>
    /// Writes the email to the log instead of sending it. Always configured, so a
    /// developer with no vendor credentials can still exercise the whole campaign flow
    /// end to end — and a misconfigured production box degrades to a visible no-op
    /// rather than a 500.
    ///
    /// Never used implicitly: the dispatcher only routes here when it is explicitly
    /// chosen or listed in the fallback order.
    /// </summary>
    public class LogEmailProvider : IEmailProvider
    {
        private readonly ILogger<LogEmailProvider> _logger;

        public LogEmailProvider(ILogger<LogEmailProvider> logger) => _logger = logger;

        public string Key => "log";
        public string DisplayName => "Log only (no delivery)";
        public bool IsConfigured => true;
        public string? ConfigurationHint => "Writes to the application log instead of delivering. Development use only.";

        public Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[log] Would send to {To} | subject: {Subject} | tag: {Tag}\n{Body}",
                email.ToEmail, email.Subject, email.Tag, email.TextBody ?? email.HtmlBody);

            return Task.FromResult(EmailSendResult.Ok(Key, $"log-{Guid.NewGuid():N}"));
        }
    }
}
