using Auth.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Services.Services.Email.Providers
{
    /// <summary>
    /// Plain SMTP — the free, no-vendor option. Works with Gmail/Workspace app passwords,
    /// Zoho, Postfix, or any relay. Reuses the SMTP_* variables the app already had.
    /// </summary>
    public class SmtpEmailProvider : SmtpProviderBase
    {
        private readonly SMTPSettings _settings;
        private readonly EmailOptions _emailOptions;

        public SmtpEmailProvider(
            IOptions<SMTPSettings> settings,
            IOptions<EmailOptions> emailOptions,
            ILogger<SmtpEmailProvider> logger) : base(logger)
        {
            _settings = settings.Value;
            _emailOptions = emailOptions.Value;
        }

        public override string Key => "smtp";
        public override string DisplayName => "SMTP (self-hosted / Gmail)";

        public override bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_settings.Host) &&
            _settings.Port > 0 &&
            !string.IsNullOrWhiteSpace(FromAddress);

        public override string? ConfigurationHint => IsConfigured
            ? null
            : "Set SMTP_HOST, SMTP_PORT and SMTP_FROM_EMAIL (plus SMTP_USERNAME / SMTP_PASSWORD if the relay requires auth).";

        private string? FromAddress => _settings.FromEmail ?? _emailOptions.FromEmail;

        protected override string Host => _settings.Host;
        protected override int Port => _settings.Port;
        protected override string? Username => _settings.Username;
        protected override string? Password => _settings.Password;
        protected override bool EnableSsl => _settings.EnableSsl;
        protected override string? DefaultFromEmail => FromAddress;
        protected override string? DefaultFromName => _settings.FromName ?? _emailOptions.FromName;
    }
}
