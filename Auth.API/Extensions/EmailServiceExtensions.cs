using Auth.Services.Interfaces.Email;
using Auth.Services.Services.Email;
using Auth.Services.Services.Email.Providers;
using Auth.Services.Settings;

namespace Auth.API.Extensions
{
    /// <summary>
    /// Wires up the pluggable email stack. Configuration comes from environment variables
    /// (DotNetEnv loads .env into the process environment before this runs, so
    /// <c>IConfiguration</c> sees both .env values and real env vars).
    ///
    /// See <c>docs/EMAIL_PROVIDERS.md</c> for every variable and how to obtain it.
    /// </summary>
    public static class EmailServiceExtensions
    {
        public static IServiceCollection AddEmailProviders(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<EmailOptions>(options =>
            {
                options.DefaultProvider = configuration["EMAIL_PROVIDER"] ?? "smtp";
                options.EnableFallback = ReadBool(configuration["EMAIL_ENABLE_FALLBACK"]);
                options.FallbackOrder = SplitCsv(configuration["EMAIL_FALLBACK_ORDER"]);
                options.FromEmail = configuration["EMAIL_FROM_ADDRESS"] ?? configuration["SMTP_FROM_EMAIL"];
                options.FromName = configuration["EMAIL_FROM_NAME"] ?? configuration["SMTP_FROM_NAME"];
                options.ReplyTo = configuration["EMAIL_REPLY_TO"];
                options.SandboxRedirectTo = configuration["EMAIL_SANDBOX_REDIRECT_TO"];
                options.SendDelayMs = ReadInt(configuration["EMAIL_SEND_DELAY_MS"], 0);
                options.MaxRecipientsPerCampaign = ReadInt(configuration["EMAIL_MAX_RECIPIENTS_PER_CAMPAIGN"], 500);
            });

            services.Configure<GmassOptions>(options =>
            {
                options.Host = configuration["GMASS_SMTP_HOST"] ?? "smtp.gmass.co";
                options.Port = ReadInt(configuration["GMASS_SMTP_PORT"], 587);
                options.Username = configuration["GMASS_SMTP_USERNAME"] ?? "gmass";
                options.ApiKey = configuration["GMASS_API_KEY"];
                options.FromEmail = configuration["GMASS_FROM_EMAIL"];
                options.FromName = configuration["GMASS_FROM_NAME"];
            });

            services.Configure<MailchimpOptions>(options =>
            {
                options.ApiKey = configuration["MAILCHIMP_TRANSACTIONAL_API_KEY"];
                options.BaseUrl = configuration["MAILCHIMP_BASE_URL"] ?? "https://mandrillapp.com/api/1.0";
                options.FromEmail = configuration["MAILCHIMP_FROM_EMAIL"];
                options.FromName = configuration["MAILCHIMP_FROM_NAME"];
                options.Subaccount = configuration["MAILCHIMP_SUBACCOUNT"];
            });

            services.Configure<EmailJsOptions>(options =>
            {
                options.ServiceId = configuration["EMAILJS_SERVICE_ID"];
                options.TemplateId = configuration["EMAILJS_TEMPLATE_ID"];
                options.PublicKey = configuration["EMAILJS_PUBLIC_KEY"];
                options.PrivateKey = configuration["EMAILJS_PRIVATE_KEY"];
                options.BaseUrl = configuration["EMAILJS_BASE_URL"] ?? "https://api.emailjs.com/api/v1.0/email/send";
            });

            services.Configure<ResendOptions>(options =>
            {
                options.ApiKey = configuration["RESEND_API_KEY"];
                options.BaseUrl = configuration["RESEND_BASE_URL"] ?? "https://api.resend.com";
                options.FromEmail = configuration["RESEND_FROM_EMAIL"];
                options.FromName = configuration["RESEND_FROM_NAME"];
            });

            // Named clients get their own connection pool and timeout, so one slow vendor
            // can't exhaust the shared handler pool and stall unrelated requests.
            services.AddHttpClient(nameof(MailchimpEmailProvider), c => c.Timeout = TimeSpan.FromSeconds(30));
            services.AddHttpClient(nameof(EmailJsEmailProvider), c => c.Timeout = TimeSpan.FromSeconds(30));
            services.AddHttpClient(nameof(ResendEmailProvider), c => c.Timeout = TimeSpan.FromSeconds(30));

            // Registration order is also the implicit fallback order when
            // EMAIL_FALLBACK_ORDER isn't set. "log" is last and never chosen implicitly.
            services.AddSingleton<IEmailProvider, SmtpEmailProvider>();
            services.AddSingleton<IEmailProvider, GmassEmailProvider>();
            services.AddSingleton<IEmailProvider, MailchimpEmailProvider>();
            services.AddSingleton<IEmailProvider, ResendEmailProvider>();
            services.AddSingleton<IEmailProvider, EmailJsEmailProvider>();
            services.AddSingleton<IEmailProvider, LogEmailProvider>();

            services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();
            services.AddSingleton<IEmailDispatcher, EmailDispatcher>();

            return services;
        }

        private static bool ReadBool(string? raw) =>
            !string.IsNullOrWhiteSpace(raw) &&
            (raw.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) || raw.Trim() == "1");

        private static int ReadInt(string? raw, int fallback) =>
            int.TryParse(raw, out var value) ? value : fallback;

        private static List<string> SplitCsv(string? raw) =>
            string.IsNullOrWhiteSpace(raw)
                ? new List<string>()
                : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }
}
