using Auth.Models.DTOs.Email;
using Auth.Services.Interfaces.Email;
using Auth.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Services.Services.Email
{
    /// <summary>
    /// Picks a provider for each send and, when enabled, retries transient failures on the
    /// next configured provider.
    ///
    /// Every provider is injected as <c>IEnumerable&lt;IEmailProvider&gt;</c>, so registering a new
    /// vendor is a one-line DI change with no edit here — that's the point of the abstraction.
    /// </summary>
    public class EmailDispatcher : IEmailDispatcher
    {
        private readonly IReadOnlyList<IEmailProvider> _providers;
        private readonly EmailOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmailDispatcher> _logger;

        public EmailDispatcher(
            IEnumerable<IEmailProvider> providers,
            IOptions<EmailOptions> options,
            IServiceScopeFactory scopeFactory,
            ILogger<EmailDispatcher> logger)
        {
            _providers = providers.ToList();
            _options = options.Value;
            // The dispatcher is a singleton but the suppression service needs a scoped
            // DbContext, so resolve one per send rather than capturing a disposed context.
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public string? DefaultProviderKey
        {
            get
            {
                var configured = Resolve(_options.DefaultProvider);
                if (configured is not null) return configured.Key;

                // Configured default is missing or unusable — fall back to whatever *is*
                // usable so the app still sends rather than silently dropping mail.
                return _providers.FirstOrDefault(p => p.IsConfigured && p.Key != "log")?.Key
                    ?? _providers.FirstOrDefault(p => p.IsConfigured)?.Key;
            }
        }

        public IReadOnlyList<IEmailProvider> GetProviders() => _providers;

        public async Task<EmailSendResult> SendAsync(
            OutboundEmail email,
            string? preferredProviderKey = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(email);

            if (string.IsNullOrWhiteSpace(email.ToEmail))
                return EmailSendResult.Fail("none", "Recipient address is empty.", isTransient: false);

            // Final gate before anything leaves the system. Deliberately here rather than in
            // each audience query: there are many of those, they are written by hand, and one
            // of them forgetting to exclude deactivated accounts is a matter of time. This
            // makes that mistake harmless instead of sending mail to someone we deactivated.
            var suppression = await CheckSuppressionAsync(email.ToEmail, cancellationToken);
            if (suppression.IsSuppressed)
            {
                _logger.LogInformation(
                    "Suppressed email to {Email}: {Reason}", email.ToEmail, suppression.Explanation);

                // Not a failure — the system worked as intended. Reported as a distinct
                // outcome so campaign stats show "skipped", not "failed".
                return EmailSendResult.Suppressed(suppression.Reason.ToString(), suppression.Explanation);
            }

            ApplyDefaults(email);

            var chain = BuildProviderChain(preferredProviderKey);
            if (chain.Count == 0)
            {
                var hint = string.Join(" | ", _providers.Select(p => $"{p.Key}: {p.ConfigurationHint ?? "ok"}"));
                _logger.LogError("No email provider is configured. {Hints}", hint);
                return EmailSendResult.Fail("none", "No email provider is configured. See docs/EMAIL_PROVIDERS.md.", isTransient: false);
            }

            EmailSendResult? last = null;

            foreach (var provider in chain)
            {
                last = await provider.SendAsync(email, cancellationToken);
                if (last.Success) return last;

                // A permanent failure (bad address, rejected domain) will fail identically
                // everywhere — retrying it just multiplies the damage and the latency.
                if (!last.IsTransient)
                {
                    _logger.LogWarning(
                        "[{Provider}] Permanent failure for {Email}, not falling back: {Error}",
                        provider.Key, email.ToEmail, last.Error);
                    return last;
                }

                _logger.LogWarning(
                    "[{Provider}] Transient failure for {Email}: {Error}",
                    provider.Key, email.ToEmail, last.Error);
            }

            return last ?? EmailSendResult.Fail("none", "No provider attempted the send.", isTransient: false);
        }

        /// <summary>
        /// Runs the suppression check in its own scope. Fails OPEN on an unexpected error:
        /// a database blip should not silently stop every password reset and 2FA code in the
        /// system. The cost of that choice is bounded — audience queries filter too, so this
        /// backstop only matters when one of them has a bug.
        /// </summary>
        private async Task<SuppressionCheck> CheckSuppressionAsync(string email, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var suppression = scope.ServiceProvider.GetRequiredService<IEmailSuppressionService>();
                return await suppression.CheckAsync(email, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Suppression check failed for {Email}; allowing the send.", email);
                return SuppressionCheck.Allowed;
            }
        }

        private void ApplyDefaults(OutboundEmail email)
        {
            email.FromEmail ??= _options.FromEmail;
            email.FromName ??= _options.FromName;
            email.ReplyTo ??= _options.ReplyTo;

            if (string.IsNullOrWhiteSpace(email.TextBody))
                email.TextBody = HtmlToText(email.HtmlBody);

            // Sandbox: redirect everything to one inbox so a broadcast can be rehearsed
            // against real data without mailing real speakers.
            if (_options.IsSandboxed)
            {
                var original = email.ToEmail;
                email.ToEmail = _options.SandboxRedirectTo!;
                email.Subject = $"[SANDBOX → {original}] {email.Subject}";
            }
        }

        /// <summary>
        /// Ordered list of providers to try: the preferred/default one first, then the
        /// fallback chain (only when fallback is enabled). Unconfigured providers are
        /// filtered out, and "log" is never added implicitly.
        /// </summary>
        private List<IEmailProvider> BuildProviderChain(string? preferredProviderKey)
        {
            var chain = new List<IEmailProvider>();

            var primary = Resolve(preferredProviderKey) ?? Resolve(_options.DefaultProvider);

            if (primary is null)
            {
                // Nothing named is usable — take the first configured non-log provider.
                primary = _providers.FirstOrDefault(p => p.IsConfigured && p.Key != "log");

                if (primary is not null && !string.IsNullOrWhiteSpace(preferredProviderKey))
                {
                    _logger.LogWarning(
                        "Requested email provider '{Requested}' is unavailable; using '{Actual}' instead.",
                        preferredProviderKey, primary.Key);
                }
            }

            if (primary is not null) chain.Add(primary);

            if (!_options.EnableFallback) return chain;

            var ordered = _options.FallbackOrder.Count > 0
                ? _options.FallbackOrder.Select(Resolve).OfType<IEmailProvider>()
                : _providers.Where(p => p.IsConfigured && p.Key != "log");

            foreach (var provider in ordered)
            {
                if (chain.Any(p => p.Key == provider.Key)) continue;
                chain.Add(provider);
            }

            return chain;
        }

        /// <summary>Looks up a configured provider by key. Returns null for unknown or unconfigured keys.</summary>
        private IEmailProvider? Resolve(string? key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

            return provider is { IsConfigured: true } ? provider : null;
        }

        /// <summary>
        /// Crude HTML→text for the plain-text alternative. Good enough for our own
        /// generated layout; not a general-purpose converter.
        /// </summary>
        private static string HtmlToText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            var text = System.Text.RegularExpressions.Regex.Replace(html, "<br\\s*/?>", "\n");
            text = System.Text.RegularExpressions.Regex.Replace(text, "</p>", "\n\n");
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty);
            text = System.Net.WebUtility.HtmlDecode(text);

            return string.Join("\n",
                text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        }
    }
}
