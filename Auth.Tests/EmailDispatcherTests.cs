using Auth.Models.DTOs.Email;
using Auth.Services.Interfaces.Email;
using Auth.Services.Services.Email;
using Auth.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth.Tests;

/// <summary>
/// Provider selection and fallback behaviour — the part of the email stack that decides
/// which vendor a message goes through, and what happens when one is down.
/// </summary>
public class EmailDispatcherTests
{
    /// <summary>Test double that records what it was asked to send and returns a canned result.</summary>
    private sealed class FakeProvider : IEmailProvider
    {
        private readonly Func<OutboundEmail, EmailSendResult> _behaviour;

        public FakeProvider(
            string key,
            bool isConfigured = true,
            Func<OutboundEmail, EmailSendResult>? behaviour = null)
        {
            Key = key;
            IsConfigured = isConfigured;
            _behaviour = behaviour ?? (_ => EmailSendResult.Ok(key));
        }

        public string Key { get; }
        public string DisplayName => Key;
        public bool IsConfigured { get; }
        public string? ConfigurationHint => IsConfigured ? null : $"{Key} not configured";

        public List<OutboundEmail> Sent { get; } = new();

        public Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken cancellationToken = default)
        {
            Sent.Add(email);
            return Task.FromResult(_behaviour(email));
        }
    }

    private static EmailDispatcher Build(EmailOptions options, params IEmailProvider[] providers) =>
        Build(options, SuppressionCheck.Allowed, providers);

    private static EmailDispatcher Build(
        EmailOptions options, SuppressionCheck suppression, params IEmailProvider[] providers) =>
        new(providers,
            Options.Create(options),
            new StubScopeFactory(new StubSuppressionService(suppression)),
            NullLogger<EmailDispatcher>.Instance);

    /// <summary>Returns a fixed verdict for every address.</summary>
    private sealed class StubSuppressionService : IEmailSuppressionService
    {
        private readonly SuppressionCheck _verdict;

        public StubSuppressionService(SuppressionCheck verdict) => _verdict = verdict;

        public Task<SuppressionCheck> CheckAsync(string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult(_verdict);

        public Task<IReadOnlyDictionary<string, SuppressionCheck>> CheckManyAsync(
            IEnumerable<string> emails, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, SuppressionCheck>>(
                new Dictionary<string, SuppressionCheck>());
    }

    /// <summary>
    /// Minimal scope factory handing the dispatcher a stub suppression service, standing in
    /// for the scoped DbContext-backed one it resolves in production.
    /// </summary>
    private sealed class StubScopeFactory : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        private readonly IEmailSuppressionService _suppression;

        public StubScopeFactory(IEmailSuppressionService suppression) => _suppression = suppression;

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => this;
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IEmailSuppressionService) ? _suppression : null;
        public void Dispose() { }
    }

    private static OutboundEmail Message() => new()
    {
        ToEmail = "speaker@example.org",
        Subject = "Hello",
        HtmlBody = "<p>Hi</p>"
    };

    [Fact]
    public async Task SendAsync_UsesTheConfiguredDefaultProvider()
    {
        var smtp = new FakeProvider("smtp");
        var resend = new FakeProvider("resend");
        var dispatcher = Build(new EmailOptions { DefaultProvider = "resend" }, smtp, resend);

        var result = await dispatcher.SendAsync(Message());

        Assert.True(result.Success);
        Assert.Equal("resend", result.Provider);
        Assert.Empty(smtp.Sent);
        Assert.Single(resend.Sent);
    }

    [Fact]
    public async Task SendAsync_PrefersAnExplicitlyRequestedProvider()
    {
        var smtp = new FakeProvider("smtp");
        var gmass = new FakeProvider("gmass");
        var dispatcher = Build(new EmailOptions { DefaultProvider = "smtp" }, smtp, gmass);

        var result = await dispatcher.SendAsync(Message(), "gmass");

        Assert.Equal("gmass", result.Provider);
        Assert.Empty(smtp.Sent);
    }

    [Fact]
    public async Task SendAsync_FallsBackToAConfiguredProvider_WhenTheRequestedOneIsNot()
    {
        var smtp = new FakeProvider("smtp");
        var mailchimp = new FakeProvider("mailchimp", isConfigured: false);
        var dispatcher = Build(new EmailOptions { DefaultProvider = "smtp" }, smtp, mailchimp);

        var result = await dispatcher.SendAsync(Message(), "mailchimp");

        Assert.True(result.Success);
        Assert.Equal("smtp", result.Provider);
    }

    [Fact]
    public async Task SendAsync_FailsCleanly_WhenNothingIsConfigured()
    {
        var dispatcher = Build(
            new EmailOptions { DefaultProvider = "smtp" },
            new FakeProvider("smtp", isConfigured: false));

        var result = await dispatcher.SendAsync(Message());

        Assert.False(result.Success);
        Assert.Contains("No email provider is configured", result.Error);
    }

    [Fact]
    public async Task SendAsync_RetriesTransientFailuresOnTheNextProvider()
    {
        var failing = new FakeProvider("smtp",
            behaviour: _ => EmailSendResult.Fail("smtp", "connection reset", isTransient: true));
        var backup = new FakeProvider("resend");

        var dispatcher = Build(new EmailOptions
        {
            DefaultProvider = "smtp",
            EnableFallback = true,
            FallbackOrder = new List<string> { "resend" }
        }, failing, backup);

        var result = await dispatcher.SendAsync(Message());

        Assert.True(result.Success);
        Assert.Equal("resend", result.Provider);
        Assert.Single(failing.Sent);
        Assert.Single(backup.Sent);
    }

    [Fact]
    public async Task SendAsync_DoesNotRetryPermanentFailures()
    {
        // A malformed address fails identically everywhere — falling back would only
        // multiply the latency and the log noise.
        var failing = new FakeProvider("smtp",
            behaviour: _ => EmailSendResult.Fail("smtp", "recipient rejected", isTransient: false));
        var backup = new FakeProvider("resend");

        var dispatcher = Build(new EmailOptions
        {
            DefaultProvider = "smtp",
            EnableFallback = true,
            FallbackOrder = new List<string> { "resend" }
        }, failing, backup);

        var result = await dispatcher.SendAsync(Message());

        Assert.False(result.Success);
        Assert.Empty(backup.Sent);
    }

    [Fact]
    public async Task SendAsync_DoesNotFallBack_WhenFallbackIsDisabled()
    {
        var failing = new FakeProvider("smtp",
            behaviour: _ => EmailSendResult.Fail("smtp", "timeout", isTransient: true));
        var backup = new FakeProvider("resend");

        var dispatcher = Build(
            new EmailOptions { DefaultProvider = "smtp", EnableFallback = false },
            failing, backup);

        var result = await dispatcher.SendAsync(Message());

        Assert.False(result.Success);
        Assert.Empty(backup.Sent);
    }

    [Fact]
    public async Task SendAsync_NeverRoutesToTheLogProviderImplicitly()
    {
        // "log" delivers nothing. Picking it automatically would look like success while
        // no speaker received anything.
        var log = new FakeProvider("log");
        var dispatcher = Build(new EmailOptions { DefaultProvider = "nonexistent" }, log);

        var result = await dispatcher.SendAsync(Message());

        Assert.False(result.Success);
        Assert.Empty(log.Sent);
    }

    [Fact]
    public async Task SendAsync_RoutesToTheLogProvider_WhenExplicitlyChosen()
    {
        var log = new FakeProvider("log");
        var dispatcher = Build(new EmailOptions { DefaultProvider = "log" }, log);

        var result = await dispatcher.SendAsync(Message());

        Assert.True(result.Success);
        Assert.Single(log.Sent);
    }

    [Fact]
    public async Task SendAsync_RedirectsEverythingInSandboxMode()
    {
        var smtp = new FakeProvider("smtp");
        var dispatcher = Build(new EmailOptions
        {
            DefaultProvider = "smtp",
            SandboxRedirectTo = "qa@example.org"
        }, smtp);

        await dispatcher.SendAsync(Message());

        var sent = Assert.Single(smtp.Sent);
        Assert.Equal("qa@example.org", sent.ToEmail);
        // The intended recipient must stay visible, or sandbox output is unreadable.
        Assert.Contains("speaker@example.org", sent.Subject);
    }

    [Fact]
    public async Task SendAsync_RejectsAnEmptyRecipient()
    {
        var smtp = new FakeProvider("smtp");
        var dispatcher = Build(new EmailOptions { DefaultProvider = "smtp" }, smtp);

        var result = await dispatcher.SendAsync(new OutboundEmail { Subject = "x", HtmlBody = "y" });

        Assert.False(result.Success);
        Assert.Empty(smtp.Sent);
    }

    [Fact]
    public async Task SendAsync_DerivesAPlainTextBodyWhenNoneIsSupplied()
    {
        var smtp = new FakeProvider("smtp");
        var dispatcher = Build(new EmailOptions { DefaultProvider = "smtp" }, smtp);

        await dispatcher.SendAsync(new OutboundEmail
        {
            ToEmail = "a@b.org",
            Subject = "s",
            HtmlBody = "<p>Hello</p><p>World</p>"
        });

        var sent = Assert.Single(smtp.Sent);
        Assert.Contains("Hello", sent.TextBody);
        Assert.DoesNotContain("<p>", sent.TextBody);
    }

    [Fact]
    public async Task SendAsync_AppliesGlobalFromDefaults()
    {
        var smtp = new FakeProvider("smtp");
        var dispatcher = Build(new EmailOptions
        {
            DefaultProvider = "smtp",
            FromEmail = "noreply@bhff.org",
            FromName = "BHFF",
            ReplyTo = "partnerships@bhff.org"
        }, smtp);

        await dispatcher.SendAsync(Message());

        var sent = Assert.Single(smtp.Sent);
        Assert.Equal("noreply@bhff.org", sent.FromEmail);
        Assert.Equal("BHFF", sent.FromName);
        Assert.Equal("partnerships@bhff.org", sent.ReplyTo);
    }

    [Fact]
    public void DefaultProviderKey_FallsBackToAnyConfiguredProvider()
    {
        var dispatcher = Build(
            new EmailOptions { DefaultProvider = "mailchimp" },
            new FakeProvider("mailchimp", isConfigured: false),
            new FakeProvider("smtp"));

        Assert.Equal("smtp", dispatcher.DefaultProviderKey);
    }

    [Fact]
    public void GetProviders_ReturnsUnconfiguredOnesToo()
    {
        // The settings screen needs to show what ISN'T set up, along with the reason.
        var dispatcher = Build(
            new EmailOptions(),
            new FakeProvider("smtp"),
            new FakeProvider("gmass", isConfigured: false));

        Assert.Equal(2, dispatcher.GetProviders().Count);
    }

    // ── Suppression ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SuppressedRecipient_NeverReachesAProvider()
    {
        // The point of enforcing suppression in the dispatcher: even with a perfectly
        // healthy provider and an audience query that forgot to filter deactivated
        // accounts, nothing goes out.
        var provider = new FakeProvider("smtp");

        var dispatcher = Build(
            new EmailOptions { DefaultProvider = "smtp" },
            SuppressionCheck.Block(SuppressionReason.UserInactive, "The account is deactivated."),
            provider);

        var result = await dispatcher.SendAsync(Message());

        Assert.Empty(provider.Sent);
        Assert.False(result.Success);
        Assert.True(result.WasSuppressed);
    }

    [Fact]
    public async Task SuppressedRecipient_IsReportedAsSkippedNotFailed()
    {
        // Campaign stats must not show a suppressed recipient as a failure: nothing went
        // wrong, and retry must not pick it up and try again.
        var dispatcher = Build(
            new EmailOptions { DefaultProvider = "smtp" },
            SuppressionCheck.Block(SuppressionReason.FirmUnsubscribed, "The firm unsubscribed."),
            new FakeProvider("smtp"));

        var result = await dispatcher.SendAsync(Message());

        Assert.True(result.WasSuppressed);
        Assert.False(result.IsTransient);
        Assert.Contains("unsubscribed", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SuppressionDoesNotTriggerProviderFallback()
    {
        // A suppressed recipient must not cascade down the fallback chain trying every
        // vendor in turn — that would be N pointless attempts per blocked address.
        var primary = new FakeProvider("smtp");
        var secondary = new FakeProvider("gmass");

        var dispatcher = Build(
            new EmailOptions { DefaultProvider = "smtp", EnableFallback = true },
            SuppressionCheck.Block(SuppressionReason.UserInactive, "Deactivated."),
            primary, secondary);

        await dispatcher.SendAsync(Message());

        Assert.Empty(primary.Sent);
        Assert.Empty(secondary.Sent);
    }
}
