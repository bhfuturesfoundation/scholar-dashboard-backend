using Auth.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Services.Services.Email.Providers
{
    /// <summary>
    /// GMass, via its SMTP relay (smtp.gmass.co). GMass authenticates with the literal
    /// username "gmass" and your API key as the password, which is why this is an SMTP
    /// provider rather than a REST one — same wire protocol, different credentials.
    ///
    /// Free tier is rate-limited; pair with EMAIL_SEND_DELAY_MS for bulk campaigns.
    /// </summary>
    public class GmassEmailProvider : SmtpProviderBase
    {
        private readonly GmassOptions _options;
        private readonly EmailOptions _emailOptions;

        public GmassEmailProvider(
            IOptions<GmassOptions> options,
            IOptions<EmailOptions> emailOptions,
            ILogger<GmassEmailProvider> logger) : base(logger)
        {
            _options = options.Value;
            _emailOptions = emailOptions.Value;
        }

        public override string Key => "gmass";
        public override string DisplayName => "GMass";

        public override bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_options.ApiKey) &&
            !string.IsNullOrWhiteSpace(FromAddress);

        public override string? ConfigurationHint => IsConfigured
            ? null
            : "Set GMASS_API_KEY and GMASS_FROM_EMAIL (the Gmail address connected to your GMass account).";

        private string? FromAddress => _options.FromEmail ?? _emailOptions.FromEmail;

        protected override string Host => _options.Host;
        protected override int Port => _options.Port;
        protected override string? Username => _options.Username;
        protected override string? Password => _options.ApiKey;
        protected override bool EnableSsl => true;
        protected override string? DefaultFromEmail => FromAddress;
        protected override string? DefaultFromName => _options.FromName ?? _emailOptions.FromName;
    }
}
