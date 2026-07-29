namespace Auth.Services.Settings
{
    /// <summary>
    /// Cross-provider email settings. Bound from environment variables in
    /// <c>EmailServiceExtensions</c>; see <c>docs/EMAIL_PROVIDERS.md</c> for the full list.
    /// </summary>
    public class EmailOptions
    {
        /// <summary>Provider key used when a caller doesn't name one (EMAIL_PROVIDER).</summary>
        public string DefaultProvider { get; set; } = "smtp";

        /// <summary>
        /// When true, a transient failure on the chosen provider is retried on the next
        /// configured provider in <see cref="FallbackOrder"/> (EMAIL_ENABLE_FALLBACK).
        /// </summary>
        public bool EnableFallback { get; set; }

        /// <summary>
        /// Ordered provider keys to try after the primary fails (EMAIL_FALLBACK_ORDER,
        /// comma-separated). Empty means "every other configured provider, registration order".
        /// </summary>
        public List<string> FallbackOrder { get; set; } = new();

        /// <summary>Default from-address applied when a provider has no override (EMAIL_FROM_ADDRESS).</summary>
        public string? FromEmail { get; set; }

        public string? FromName { get; set; }

        public string? ReplyTo { get; set; }

        /// <summary>
        /// When set, EVERY outbound email is redirected to this address instead of the real
        /// recipient, with the original recipient noted in the subject (EMAIL_SANDBOX_REDIRECT_TO).
        /// This is the safety catch for testing a broadcast without mailing 200 real speakers.
        /// </summary>
        public string? SandboxRedirectTo { get; set; }

        /// <summary>
        /// Milliseconds to wait between sends in a bulk campaign (EMAIL_SEND_DELAY_MS).
        /// Free tiers (EmailJS, GMass) throttle aggressively; pacing beats getting blocked.
        /// </summary>
        public int SendDelayMs { get; set; }

        /// <summary>Hard cap on recipients in one campaign (EMAIL_MAX_RECIPIENTS_PER_CAMPAIGN).</summary>
        public int MaxRecipientsPerCampaign { get; set; } = 500;

        public bool IsSandboxed => !string.IsNullOrWhiteSpace(SandboxRedirectTo);
    }

    public class GmassOptions
    {
        /// <summary>GMass SMTP relay host — smtp.gmass.co unless GMass tells you otherwise.</summary>
        public string Host { get; set; } = "smtp.gmass.co";
        public int Port { get; set; } = 587;

        /// <summary>GMass requires the literal username "gmass".</summary>
        public string Username { get; set; } = "gmass";

        /// <summary>Your GMass API key, used as the SMTP password (GMASS_API_KEY).</summary>
        public string? ApiKey { get; set; }

        public string? FromEmail { get; set; }
        public string? FromName { get; set; }
    }

    public class MailchimpOptions
    {
        /// <summary>Mailchimp Transactional (Mandrill) API key (MAILCHIMP_TRANSACTIONAL_API_KEY).</summary>
        public string? ApiKey { get; set; }

        public string BaseUrl { get; set; } = "https://mandrillapp.com/api/1.0";

        public string? FromEmail { get; set; }
        public string? FromName { get; set; }

        /// <summary>Optional Mandrill subaccount for per-tenant reporting.</summary>
        public string? Subaccount { get; set; }
    }

    public class EmailJsOptions
    {
        public string? ServiceId { get; set; }
        public string? TemplateId { get; set; }
        public string? PublicKey { get; set; }

        /// <summary>
        /// Required for server-side sends. EmailJS rejects API calls from a non-browser
        /// origin unless the private key is supplied (EMAILJS_PRIVATE_KEY).
        /// </summary>
        public string? PrivateKey { get; set; }

        public string BaseUrl { get; set; } = "https://api.emailjs.com/api/v1.0/email/send";
    }

    public class ResendOptions
    {
        public string? ApiKey { get; set; }
        public string BaseUrl { get; set; } = "https://api.resend.com";
        public string? FromEmail { get; set; }
        public string? FromName { get; set; }
    }
}
