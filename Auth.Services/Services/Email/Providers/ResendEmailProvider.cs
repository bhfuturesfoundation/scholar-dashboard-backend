using Auth.Models.DTOs.Email;
using Auth.Services.Interfaces.Email;
using Auth.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Auth.Services.Services.Email.Providers
{
    /// <summary>
    /// Resend — generous free tier (3k/month), paid beyond that. Simple REST API,
    /// good middle ground between EmailJS's limits and Mailchimp's price.
    /// </summary>
    public class ResendEmailProvider : IEmailProvider
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ResendOptions _options;
        private readonly EmailOptions _emailOptions;
        private readonly ILogger<ResendEmailProvider> _logger;

        public ResendEmailProvider(
            IHttpClientFactory httpClientFactory,
            IOptions<ResendOptions> options,
            IOptions<EmailOptions> emailOptions,
            ILogger<ResendEmailProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _emailOptions = emailOptions.Value;
            _logger = logger;
        }

        public string Key => "resend";
        public string DisplayName => "Resend";

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_options.ApiKey) &&
            !string.IsNullOrWhiteSpace(FromAddress);

        public string? ConfigurationHint => IsConfigured
            ? null
            : "Set RESEND_API_KEY and RESEND_FROM_EMAIL (the domain must be verified in Resend).";

        private string? FromAddress => _options.FromEmail ?? _emailOptions.FromEmail;

        public async Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
                return EmailSendResult.Fail(Key, ConfigurationHint ?? "Provider is not configured.", isTransient: false);

            var fromEmail = email.FromEmail ?? FromAddress;
            var fromName = email.FromName ?? _options.FromName ?? _emailOptions.FromName;

            var payload = new Dictionary<string, object?>
            {
                ["from"] = string.IsNullOrWhiteSpace(fromName) ? fromEmail : $"{fromName} <{fromEmail}>",
                ["to"] = new[] { email.ToEmail },
                ["subject"] = email.Subject,
                ["html"] = email.HtmlBody,
                ["text"] = email.TextBody,
                ["reply_to"] = email.ReplyTo,
                ["tags"] = string.IsNullOrWhiteSpace(email.Tag)
                    ? null
                    : new[] { new { name = "campaign", value = Sanitize(email.Tag) } }
            };

            try
            {
                var client = _httpClientFactory.CreateClient(nameof(ResendEmailProvider));
                using var request = new HttpRequestMessage(
                    HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/emails")
                {
                    Content = JsonContent.Create(payload)
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

                var response = await client.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var transient = (int)response.StatusCode >= 500 || (int)response.StatusCode == 429;
                    return EmailSendResult.Fail(Key, $"HTTP {(int)response.StatusCode}: {Truncate(body)}", transient);
                }

                string? id = null;
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("id", out var idProp))
                        id = idProp.GetString();
                }
                catch { /* id is a nicety, not worth failing the send over */ }

                _logger.LogInformation("[resend] Email sent to {Email} (id={Id})", email.ToEmail, id);
                return EmailSendResult.Ok(Key, id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[resend] Failed to send to {Email}", email.ToEmail);
                return EmailSendResult.Fail(Key, ex.Message);
            }
        }

        /// <summary>Resend tag values only accept ASCII letters, digits, underscore and dash.</summary>
        private static string Sanitize(string value) =>
            new(value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').Take(64).ToArray());

        private static string Truncate(string value) =>
            value.Length <= 400 ? value : value[..400] + "…";
    }
}
