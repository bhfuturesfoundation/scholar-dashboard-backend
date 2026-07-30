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

        /// <summary>
        /// Hosts and addresses that are obviously scaffolding rather than real settings.
        ///
        /// Checked because "is this variable non-empty" is not the same question as "will
        /// this actually send". Production had SMTP_HOST=smtp.example.com and
        /// SMTP_FROM_EMAIL=no-reply@example.com left over from the template, which passed a
        /// non-empty check and made the health screen report a working provider — so the
        /// first campaign would have failed at send time with a DNS error instead of the
        /// settings screen saying plainly that SMTP was never configured.
        /// </summary>
        private static readonly string[] PlaceholderMarkers =
        {
            "example.com", "example.org", "your-smtp", "changeme", "smtp.host", "localhost"
        };

        public override bool IsConfigured
        {
            get
            {
                // SMTP_ENABLED existed in configuration but was read nowhere, so setting it
                // to false had no effect at all.
                if (!_settings.Enabled) return false;

                if (string.IsNullOrWhiteSpace(_settings.Host)) return false;
                if (_settings.Port <= 0) return false;
                if (string.IsNullOrWhiteSpace(FromAddress)) return false;

                return !LooksLikePlaceholder(_settings.Host) && !LooksLikePlaceholder(FromAddress!);
            }
        }

        public override string? ConfigurationHint
        {
            get
            {
                if (IsConfigured) return null;

                if (!_settings.Enabled)
                    return "SMTP_ENABLED is false. Set it to true once a real relay is configured.";

                if (!string.IsNullOrWhiteSpace(_settings.Host) && LooksLikePlaceholder(_settings.Host))
                    return $"SMTP_HOST is still the placeholder \"{_settings.Host}\". Point it at a real relay.";

                if (!string.IsNullOrWhiteSpace(FromAddress) && LooksLikePlaceholder(FromAddress!))
                    return $"SMTP_FROM_EMAIL is still the placeholder \"{FromAddress}\". Use a real sending address.";

                return "Set SMTP_HOST, SMTP_PORT and SMTP_FROM_EMAIL (plus SMTP_USERNAME / SMTP_PASSWORD if the relay requires auth).";
            }
        }

        private static bool LooksLikePlaceholder(string value) =>
            PlaceholderMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

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
