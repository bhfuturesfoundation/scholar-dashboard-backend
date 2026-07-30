using Auth.Models.DTOs.Email;
using Auth.Services.Interfaces.Email;
using Auth.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace Auth.Services.Services.Email.Providers
{
    /// <summary>
    /// EmailJS — the free-tier option (200 emails/month at time of writing).
    ///
    /// Two traps worth knowing before you pick this one:
    ///  1. Server-side calls REQUIRE the private key. EmailJS blocks non-browser origins
    ///     with "API calls are disabled for non-browser applications" unless you send
    ///     accessToken, and you must also tick "Allow requests from non-browser" in the
    ///     EmailJS dashboard. This is the single most common setup failure.
    ///  2. Subject and body are controlled by the EmailJS *template*, not by us. This
    ///     provider passes them as template params (subject/message_html/message), so your
    ///     template must reference {{subject}} and {{{message_html}}} — triple braces, or
    ///     EmailJS escapes your HTML into visible tags.
    /// </summary>
    public class EmailJsEmailProvider : IEmailProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly EmailJsOptions _options;
        private readonly ILogger<EmailJsEmailProvider> _logger;

        public EmailJsEmailProvider(
            IHttpClientFactory httpClientFactory,
            IOptions<EmailJsOptions> options,
            ILogger<EmailJsEmailProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
        }

        public string Key => "emailjs";
        public string DisplayName => "EmailJS (free tier)";

        /// <summary>
        /// The private key is part of being configured, not a nice-to-have.
        ///
        /// This previously reported configured without it, with the missing key mentioned
        /// only in the hint. That is the wrong shape: the dispatcher routes to whatever is
        /// "configured", so a deployment with the first three variables set would have been
        /// chosen as the provider and then had every single send rejected by EmailJS with
        /// "API calls are disabled for non-browser applications". Reporting it as
        /// unconfigured means the health screen says so up front and the dispatcher picks
        /// something that can actually deliver.
        /// </summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_options.ServiceId) &&
            !string.IsNullOrWhiteSpace(_options.TemplateId) &&
            !string.IsNullOrWhiteSpace(_options.PublicKey) &&
            !string.IsNullOrWhiteSpace(_options.PrivateKey);

        public string? ConfigurationHint
        {
            get
            {
                if (IsConfigured) return null;

                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(_options.ServiceId)) missing.Add("EMAILJS_SERVICE_ID");
                if (string.IsNullOrWhiteSpace(_options.TemplateId)) missing.Add("EMAILJS_TEMPLATE_ID");
                if (string.IsNullOrWhiteSpace(_options.PublicKey)) missing.Add("EMAILJS_PUBLIC_KEY");

                if (string.IsNullOrWhiteSpace(_options.PrivateKey))
                {
                    missing.Add("EMAILJS_PRIVATE_KEY");

                    if (missing.Count == 1)
                    {
                        return "EMAILJS_PRIVATE_KEY is missing. EmailJS rejects server-side sends without it — " +
                               "also tick \"Allow EmailJS API for non-browser applications\" in Account → Security.";
                    }
                }

                return $"Missing {string.Join(", ", missing)}.";
            }
        }

        public async Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                return EmailSendResult.Fail(Key, ConfigurationHint ?? "Provider is not configured.", isTransient: false);

            var payload = new
            {
                service_id = _options.ServiceId,
                template_id = _options.TemplateId,
                user_id = _options.PublicKey,
                accessToken = _options.PrivateKey,
                template_params = new Dictionary<string, string?>
                {
                    ["email"] = email.ToEmail,
                    ["to_email"] = email.ToEmail,
                    ["to_name"] = email.ToName,
                    ["subject"] = email.Subject,
                    ["message_html"] = email.HtmlBody,
                    ["message"] = email.TextBody ?? email.HtmlBody,
                    ["reply_to"] = email.ReplyTo,
                    ["from_name"] = email.FromName
                }
            };

            try
            {
                var client = _httpClientFactory.CreateClient(nameof(EmailJsEmailProvider));
                var response = await client.PostAsJsonAsync(_options.BaseUrl, payload, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    var transient = (int)response.StatusCode >= 500 || (int)response.StatusCode == 429;
                    return EmailSendResult.Fail(Key, $"HTTP {(int)response.StatusCode}: {Truncate(body)}", transient);
                }

                _logger.LogInformation("[emailjs] Email sent to {Email}", email.ToEmail);
                return EmailSendResult.Ok(Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[emailjs] Failed to send to {Email}", email.ToEmail);
                return EmailSendResult.Fail(Key, ex.Message);
            }
        }

        private static string Truncate(string value) =>
            value.Length <= 400 ? value : value[..400] + "…";
    }
}
