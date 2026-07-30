using Auth.Models.DTOs.Email;
using Auth.Services.Interfaces.Email;
using Auth.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace Auth.Services.Services.Email.Providers
{
    /// <summary>
    /// Mailchimp Transactional (formerly Mandrill) — the paid, high-deliverability option.
    ///
    /// Note this is Transactional, NOT Mailchimp Marketing. Marketing is list/campaign based
    /// and is the wrong tool for "email these 12 speakers right now"; Transactional sends
    /// individual messages on demand, which is what this feature needs.
    /// </summary>
    public class MailchimpEmailProvider : IEmailProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly MailchimpOptions _options;
        private readonly EmailOptions _emailOptions;
        private readonly ILogger<MailchimpEmailProvider> _logger;

        public MailchimpEmailProvider(
            IHttpClientFactory httpClientFactory,
            IOptions<MailchimpOptions> options,
            IOptions<EmailOptions> emailOptions,
            ILogger<MailchimpEmailProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _emailOptions = emailOptions.Value;
            _logger = logger;
        }

        public string Key => "mailchimp";
        public string DisplayName => "Mailchimp Transactional (Mandrill)";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_options.ApiKey) &&
            !string.IsNullOrWhiteSpace(FromAddress);

        public string? ConfigurationHint => IsConfigured
            ? null
            : "Set MAILCHIMP_TRANSACTIONAL_API_KEY and MAILCHIMP_FROM_EMAIL (the from-address must be on a verified sending domain).";

        private string? FromAddress => _options.FromEmail ?? _emailOptions.FromEmail;

        public async Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                return EmailSendResult.Fail(Key, ConfigurationHint ?? "Provider is not configured.", isTransient: false);

            var payload = new
            {
                key = _options.ApiKey,
                message = new
                {
                    html = email.HtmlBody,
                    text = email.TextBody,
                    subject = email.Subject,
                    from_email = email.FromEmail ?? FromAddress,
                    from_name = email.FromName ?? _options.FromName ?? _emailOptions.FromName,
                    to = new[]
                    {
                        new { email = email.ToEmail, name = email.ToName, type = "to" }
                    },
                    headers = string.IsNullOrWhiteSpace(email.ReplyTo)
                        ? null
                        : new Dictionary<string, string> { ["Reply-To"] = email.ReplyTo },
                    tags = string.IsNullOrWhiteSpace(email.Tag) ? null : new[] { email.Tag },
                    subaccount = _options.Subaccount,
                    track_opens = true,
                    track_clicks = true
                }
            };

            try
            {
                var client = _httpClientFactory.CreateClient(nameof(MailchimpEmailProvider));
                var response = await client.PostAsJsonAsync(
                    $"{_options.BaseUrl.TrimEnd('/')}/messages/send.json", payload, cancellationToken);

                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    // 4xx other than 429 means the request itself is wrong (bad key, unverified
                    // domain) — no other provider is going to fix that, so don't burn a fallback.
                    var transient = (int)response.StatusCode >= 500 || (int)response.StatusCode == 429;
                    return EmailSendResult.Fail(Key, $"HTTP {(int)response.StatusCode}: {Truncate(body)}", transient);
                }

                // Mandrill returns 200 with a per-recipient status array; "rejected"/"invalid"
                // are failures despite the 200, so the body has to be inspected.
                var (ok, status, messageId) = ParseResponse(body);
                if (!ok)
                    return EmailSendResult.Fail(Key, $"Mailchimp status '{status}' for {email.ToEmail}", isTransient: false);

                _logger.LogInformation("[mailchimp] Email sent to {Email} (status={Status})", email.ToEmail, status);
                return EmailSendResult.Ok(Key, messageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[mailchimp] Failed to send to {Email}", email.ToEmail);
                return EmailSendResult.Fail(Key, ex.Message);
            }
        }

        private static (bool ok, string status, string? messageId) ParseResponse(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return (false, "empty response", null);

                var first = doc.RootElement[0];
                var status = first.TryGetProperty("status", out var s) ? s.GetString() ?? "unknown" : "unknown";
                var id = first.TryGetProperty("_id", out var i) ? i.GetString() : null;

                // "sent" and "queued" are both successes; "scheduled" too. The rest are not.
                var ok = status is "sent" or "queued" or "scheduled";
                return (ok, status, id);
            }
            catch
            {
                return (false, "unparseable response", null);
            }
        }

        private static string Truncate(string value) =>
            value.Length <= 400 ? value : value[..400] + "…";
    }
}
