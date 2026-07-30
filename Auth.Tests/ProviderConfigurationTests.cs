using Auth.Services.Services.Email.Providers;
using Auth.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Auth.Tests;

/// <summary>
/// Tests that "configured" means "can actually deliver".
///
/// The bug these guard against was found in the live deployment: SMTP_HOST was still
/// smtp.example.com and SMTP_FROM_EMAIL still no-reply@example.com from the project
/// template, and SMTP_ENABLED was false but read nowhere. A plain non-empty check therefore
/// reported SMTP as a working provider, so the health screen showed a healthy email setup
/// and the dispatcher would have routed real campaign mail into a DNS failure.
///
/// EmailJS had the same shape: three of four variables set was reported as configured, with
/// the missing private key mentioned only in a hint nobody reads — while EmailJS rejects
/// every server-side send without it.
/// </summary>
public class ProviderConfigurationTests
{
    private static SmtpEmailProvider Smtp(SMTPSettings settings) =>
        new(Options.Create(settings), Options.Create(new EmailOptions()), NullLogger<SmtpEmailProvider>.Instance);

    private static SMTPSettings WorkingSmtp() => new()
    {
        Host = "smtp.postmarkapp.com",
        Port = 587,
        Username = "user",
        Password = "pass",
        EnableSsl = true,
        FromEmail = "partnerships@bhfuturesfoundation.org",
        FromName = "BH Futures Foundation",
        Enabled = true
    };

    // ── SMTP ──────────────────────────────────────────────────────────────────

    [Fact]
    public void RealSmtpSettings_AreConfigured()
    {
        Assert.True(Smtp(WorkingSmtp()).IsConfigured);
    }

    [Fact]
    public void SmtpDisabled_IsNotConfigured()
    {
        var settings = WorkingSmtp();
        settings.Enabled = false;

        var provider = Smtp(settings);

        Assert.False(provider.IsConfigured);
        Assert.Contains("SMTP_ENABLED", provider.ConfigurationHint);
    }

    [Theory]
    [InlineData("smtp.example.com")]
    [InlineData("SMTP.EXAMPLE.COM")]
    [InlineData("your-smtp-host.com")]
    [InlineData("localhost")]
    public void PlaceholderHost_IsNotConfigured(string host)
    {
        // The exact value found in production. Non-empty, and completely unable to send.
        var settings = WorkingSmtp();
        settings.Host = host;

        var provider = Smtp(settings);

        Assert.False(provider.IsConfigured);
        Assert.Contains("placeholder", provider.ConfigurationHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlaceholderFromAddress_IsNotConfigured()
    {
        var settings = WorkingSmtp();
        settings.FromEmail = "no-reply@example.com";

        var provider = Smtp(settings);

        Assert.False(provider.IsConfigured);
        Assert.Contains("placeholder", provider.ConfigurationHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SmtpEnabled_DefaultsToTrue()
    {
        // Deployments predating the variable must keep working.
        Assert.True(new SMTPSettings().Enabled);
    }

    [Fact]
    public void ConfiguredProvider_HasNoHint()
    {
        // The settings screen shows the hint only when something is wrong; a stale hint on a
        // working provider would send someone chasing a non-problem.
        Assert.Null(Smtp(WorkingSmtp()).ConfigurationHint);
    }

    // ── EmailJS ───────────────────────────────────────────────────────────────

    private static EmailJsEmailProvider EmailJs(EmailJsOptions options) =>
        new(new NoopHttpClientFactory(), Options.Create(options), NullLogger<EmailJsEmailProvider>.Instance);

    private static EmailJsOptions WorkingEmailJs() => new()
    {
        ServiceId = "service_abc",
        TemplateId = "template_abc",
        PublicKey = "public_abc",
        PrivateKey = "private_abc"
    };

    [Fact]
    public void CompleteEmailJs_IsConfigured()
    {
        Assert.True(EmailJs(WorkingEmailJs()).IsConfigured);
    }

    [Fact]
    public void EmailJsWithoutPrivateKey_IsNotConfigured()
    {
        // The exact production state. Server-side sends are rejected outright, so reporting
        // this as configured would let the dispatcher choose a provider that cannot deliver.
        var options = WorkingEmailJs();
        options.PrivateKey = null;

        var provider = EmailJs(options);

        Assert.False(provider.IsConfigured);
        Assert.Contains("EMAILJS_PRIVATE_KEY", provider.ConfigurationHint);
    }

    [Fact]
    public void EmailJsHint_NamesEveryMissingVariable()
    {
        var provider = EmailJs(new EmailJsOptions { ServiceId = "service_abc" });

        var hint = provider.ConfigurationHint!;

        Assert.Contains("EMAILJS_TEMPLATE_ID", hint);
        Assert.Contains("EMAILJS_PUBLIC_KEY", hint);
        Assert.Contains("EMAILJS_PRIVATE_KEY", hint);
        Assert.DoesNotContain("EMAILJS_SERVICE_ID", hint);
    }

    private sealed class NoopHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
