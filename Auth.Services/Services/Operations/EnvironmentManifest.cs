namespace Auth.Services.Services.Operations
{
    /// <summary>How badly the app needs a variable.</summary>
    public enum EnvVarImportance
    {
        /// <summary>The app cannot function without it.</summary>
        Required = 0,

        /// <summary>A feature is disabled without it, but the app runs.</summary>
        Recommended = 1,

        /// <summary>Purely optional tuning.</summary>
        Optional = 2
    }

    public record EnvVarDefinition(
        string Name,
        string Category,
        EnvVarImportance Importance,
        string Purpose,
        string? ConsequenceIfMissing = null);

    /// <summary>
    /// The curated list of environment variables this application reads.
    ///
    /// Deliberately a hand-maintained manifest rather than an enumeration of
    /// <c>Environment.GetEnvironmentVariables()</c>. Enumerating would dump every variable
    /// in the container to the browser — Railway injects database passwords, internal
    /// service tokens and platform metadata, and a future secret would be exposed the moment
    /// it was added, without anyone deciding to expose it. A manifest can only ever reveal
    /// what someone chose to list, and it can carry the "what breaks without this" copy that
    /// makes the screen actually useful.
    ///
    /// VALUES ARE NEVER READ OR RETURNED — only whether something is set. See
    /// <c>OperationsService.GetEnvironmentStatus</c>.
    /// </summary>
    public static class EnvironmentManifest
    {
        public const string Database = "Database";
        public const string Authentication = "Authentication";
        public const string Email = "Email delivery";
        public const string Integrations = "Integrations";
        public const string Infrastructure = "Infrastructure";
        public const string Seeding = "Seeding";

        public static readonly IReadOnlyList<EnvVarDefinition> All = new List<EnvVarDefinition>
        {
            // ── Database ──────────────────────────────────────────────────────
            new("PGHOST", Database, EnvVarImportance.Required,
                "PostgreSQL host. Its presence is also what switches the app into Railway mode.",
                "Falls back to a local database, which in production means an empty app."),
            new("PGPORT", Database, EnvVarImportance.Recommended,
                "PostgreSQL port.", "Defaults to 5432."),
            new("PGDATABASE", Database, EnvVarImportance.Required, "Database name."),
            new("PGUSER", Database, EnvVarImportance.Required, "Database user."),
            new("PGPASSWORD", Database, EnvVarImportance.Required, "Database password."),

            // ── Authentication ────────────────────────────────────────────────
            new("JWT_SECRET", Authentication, EnvVarImportance.Required,
                "Signing key for access tokens.",
                "Every login and every authenticated request fails."),
            new("JWT_ISSUER", Authentication, EnvVarImportance.Required,
                "Expected token issuer.", "Tokens are rejected as invalid."),
            new("JWT_AUDIENCE", Authentication, EnvVarImportance.Required,
                "Expected token audience.", "Tokens are rejected as invalid."),

            // ── Email ─────────────────────────────────────────────────────────
            new("EMAIL_PROVIDER", Email, EnvVarImportance.Recommended,
                "Which provider sends by default: smtp, gmass, mailchimp, resend, emailjs or log.",
                "Falls back to the first configured provider."),
            new("EMAIL_FROM_ADDRESS", Email, EnvVarImportance.Recommended,
                "Default from-address when a provider has no override.",
                "Providers without their own from-address cannot send."),
            new("EMAIL_FROM_NAME", Email, EnvVarImportance.Optional, "Display name on outgoing mail."),
            new("EMAIL_ENABLE_FALLBACK", Email, EnvVarImportance.Optional,
                "Retry a transient failure on the next configured provider."),
            new("EMAIL_SANDBOX_REDIRECT_TO", Email, EnvVarImportance.Optional,
                "Redirects ALL outgoing mail to one address. Safety catch for rehearsing a broadcast.",
                "Set this in staging; leaving it set in production means nobody receives mail."),
            new("EMAIL_SEND_DELAY_MS", Email, EnvVarImportance.Optional,
                "Pause between sends in a bulk campaign, to stay under free-tier rate limits."),

            new("SMTP_HOST", Email, EnvVarImportance.Optional, "SMTP relay host."),
            new("SMTP_PORT", Email, EnvVarImportance.Optional, "SMTP relay port."),
            new("SMTP_USERNAME", Email, EnvVarImportance.Optional, "SMTP username."),
            new("SMTP_PASSWORD", Email, EnvVarImportance.Optional, "SMTP password or app password."),
            new("SMTP_FROM_EMAIL", Email, EnvVarImportance.Optional, "From-address for the SMTP provider."),

            new("GMASS_API_KEY", Email, EnvVarImportance.Optional, "GMass API key, used as the SMTP password."),
            new("GMASS_FROM_EMAIL", Email, EnvVarImportance.Optional, "Gmail address connected to GMass."),

            new("MAILCHIMP_TRANSACTIONAL_API_KEY", Email, EnvVarImportance.Optional,
                "Mailchimp Transactional (Mandrill) key. Note: Transactional, not Marketing."),
            new("MAILCHIMP_FROM_EMAIL", Email, EnvVarImportance.Optional,
                "From-address on a verified Mailchimp sending domain."),

            new("RESEND_API_KEY", Email, EnvVarImportance.Optional, "Resend API key."),
            new("RESEND_FROM_EMAIL", Email, EnvVarImportance.Optional, "From-address on a verified Resend domain."),

            new("EMAILJS_SERVICE_ID", Email, EnvVarImportance.Optional, "EmailJS service id."),
            new("EMAILJS_TEMPLATE_ID", Email, EnvVarImportance.Optional, "EmailJS template id."),
            new("EMAILJS_PUBLIC_KEY", Email, EnvVarImportance.Optional, "EmailJS public key."),
            new("EMAILJS_PRIVATE_KEY", Email, EnvVarImportance.Optional,
                "EmailJS private key. Required for server-side sends.",
                "EmailJS rejects the send as a non-browser application."),

            // ── Integrations ──────────────────────────────────────────────────
            new("DROPBOX_APP_KEY", Integrations, EnvVarImportance.Recommended,
                "Dropbox app key.", "Password exports and backup uploads are skipped."),
            new("DROPBOX_APP_SECRET", Integrations, EnvVarImportance.Recommended,
                "Dropbox app secret.", "Password exports and backup uploads are skipped."),
            new("DROPBOX_REFRESH_TOKEN", Integrations, EnvVarImportance.Recommended,
                "Permanent Dropbox refresh token. Access tokens are minted from it on demand.",
                "Password exports and backup uploads are skipped."),
            new("GOOGLE_SHEETS_CREDENTIALS", Integrations, EnvVarImportance.Optional,
                "Service-account JSON for the volunteering leaderboard."),
            new("SPREADSHEET_ID", Integrations, EnvVarImportance.Optional,
                "Google Sheet backing the volunteering leaderboard."),

            // ── Infrastructure ────────────────────────────────────────────────
            new("REDIS_URL", Infrastructure, EnvVarImportance.Optional,
                "Redis backplane for SignalR.",
                "Hubs run in-memory, so real-time features break across multiple instances."),
            new("RABBITMQ_URL", Infrastructure, EnvVarImportance.Optional,
                "Message broker for queued email.",
                "Queued email falls back to the no-op broker and is not delivered."),
            new("ASPNETCORE_ENVIRONMENT", Infrastructure, EnvVarImportance.Recommended,
                "Development or Production. Controls Swagger and detailed errors.",
                "Defaults to Production."),

            // ── Seeding ───────────────────────────────────────────────────────
            new("SEED_PARTNER_MEMBER_PASSWORD", Seeding, EnvVarImportance.Optional,
                "Initial password for the seeded partnerships account. Ignored after creation."),
        };

        public static IEnumerable<string> Categories => All
            .Select(v => v.Category)
            .Distinct();
    }
}
