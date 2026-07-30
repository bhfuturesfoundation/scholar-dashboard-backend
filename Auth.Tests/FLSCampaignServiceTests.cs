using Auth.Models.Data;
using Auth.Models.DTOs.Email;
using Auth.Models.Entities;
using Auth.Models.Entities.FLS;
using Auth.Models.Enums.FLS;
using Auth.Models.Request.FLS;
using Auth.Services.Interfaces.Email;
using Auth.Services.Services.Email;
using Auth.Services.Services.FLS;
using Auth.Services.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Auth.Tests;

/// <summary>
/// End-to-end behaviour of the campaign feature against an in-memory database:
/// audience resolution, per-recipient personalisation, and the delivery record.
/// </summary>
public class FLSCampaignServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RecordingDispatcher _dispatcher = new();

    public FLSCampaignServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            // A unique database per test class instance — xUnit creates one per test, so
            // tests never see each other's rows.
            .UseInMemoryDatabase($"campaigns-{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        Seed();
    }

    public void Dispose() => _context.Dispose();

    /// <summary>Captures every dispatched email instead of sending it.</summary>
    private sealed class RecordingDispatcher : IEmailDispatcher
    {
        public List<(OutboundEmail Email, string? Provider)> Sent { get; } = new();
        public HashSet<string> FailFor { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? DefaultProviderKey => "smtp";
        public IReadOnlyList<IEmailProvider> GetProviders() => Array.Empty<IEmailProvider>();

        public Task<EmailSendResult> SendAsync(
            OutboundEmail email, string? preferredProviderKey = null, CancellationToken ct = default)
        {
            Sent.Add((email, preferredProviderKey));

            return Task.FromResult(FailFor.Contains(email.ToEmail)
                ? EmailSendResult.Fail("smtp", "mailbox full")
                : EmailSendResult.Ok(preferredProviderKey ?? "smtp", "msg-1"));
        }
    }

    private void Seed()
    {
        var amina = new User { Id = "u1", Email = "amina@example.org", FirstName = "Amina", LastName = "Hodzic", IsActive = true };
        var luka = new User { Id = "u2", Email = "luka@example.org", FirstName = "Luka", LastName = "Maric", IsActive = true };
        var ida = new User { Id = "u3", Email = "ida@example.org", FirstName = "Ida", LastName = "Begic", IsActive = true };

        _context.Users.AddRange(amina, luka, ida);

        // Amina: complete plenary speaker.
        var aminaProfile = new SpeakerProfile
        {
            Id = 1, UserId = "u1", User = amina,
            SpeakerType = SpeakerType.Plenary, Organization = "Example Org"
        };
        foreach (var type in new[] { UploadType.CV, UploadType.Picture, UploadType.Synopsis, UploadType.Presentation })
        {
            aminaProfile.Uploads.Add(new SpeakerUpload
            {
                SpeakerProfileId = 1, UploadType = type,
                OriginalFileName = $"{type}.pdf", MimeType = "application/pdf"
            });
        }

        // Luka: track speaker, missing everything.
        var lukaProfile = new SpeakerProfile
        {
            Id = 2, UserId = "u2", User = luka, SpeakerType = SpeakerType.Track
        };

        // Ida: deregistered.
        var idaProfile = new SpeakerProfile
        {
            Id = 3, UserId = "u3", User = ida,
            SpeakerType = SpeakerType.Panel, IsDeregistered = true
        };

        _context.SpeakerProfiles.AddRange(aminaProfile, lukaProfile, idaProfile);
        _context.SaveChanges();
    }

    private FLSCampaignService BuildService(EmailOptions? options = null)
    {
        var store = new Mock<IUserStore<User>>();
        var userManager = new Mock<UserManager<User>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        // No staff accounts by default — the staff audience is covered separately.
        userManager
            .Setup(m => m.GetUsersInRoleAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<User>());

        return new FLSCampaignService(
            _context,
            userManager.Object,
            _dispatcher,
            new EmailTemplateRenderer(),
            Options.Create(options ?? new EmailOptions()),
            NullLogger<FLSCampaignService>.Instance);
    }

    private static SendCampaignRequest Request(
        CampaignAudience audience = CampaignAudience.ActiveSpeakers,
        string body = "Dear {{firstName}}, welcome.") => new()
    {
        Subject = "FLS {{year}}",
        Body = body,
        Audience = audience,
        AlsoCreateInAppNotification = false
    };

    // ── Audience resolution ──────────────────────────────────────────────────

    [Fact]
    public async Task Send_ActiveSpeakers_ExcludesDeregistered()
    {
        var result = await BuildService().SendAsync(Request(), "admin", "Admin");

        Assert.Equal(2, result.TotalRecipients);
        Assert.DoesNotContain(result.Recipients, r => r.Email == "ida@example.org");
    }

    [Fact]
    public async Task Send_IncompleteUploads_OnlyTargetsSpeakersMissingSomething()
    {
        var result = await BuildService()
            .SendAsync(Request(CampaignAudience.SpeakersWithIncompleteUploads), "admin", "Admin");

        var recipient = Assert.Single(result.Recipients);
        Assert.Equal("luka@example.org", recipient.Email);
    }

    [Fact]
    public async Task Send_ByType_FiltersOnSpeakerType()
    {
        var request = Request(CampaignAudience.SpeakersByType);
        request.SpeakerTypeFilter = SpeakerType.Plenary;

        var result = await BuildService().SendAsync(request, "admin", "Admin");

        Assert.Single(result.Recipients);
        Assert.Equal("amina@example.org", result.Recipients[0].Email);
    }

    [Fact]
    public async Task Send_Deregistered_TargetsOnlyWithdrawnSpeakers()
    {
        var result = await BuildService()
            .SendAsync(Request(CampaignAudience.DeregisteredSpeakers), "admin", "Admin");

        Assert.Single(result.Recipients);
        Assert.Equal("ida@example.org", result.Recipients[0].Email);
    }

    [Fact]
    public async Task Send_SelectedSpeakers_TargetsExactlyTheChosenIds()
    {
        var request = Request(CampaignAudience.SelectedSpeakers);
        request.SpeakerProfileIds = new List<int> { 2 };

        var result = await BuildService().SendAsync(request, "admin", "Admin");

        Assert.Single(result.Recipients);
        Assert.Equal("luka@example.org", result.Recipients[0].Email);
    }

    [Fact]
    public async Task Send_SelectedSpeakers_RequiresAtLeastOneId()
    {
        var request = Request(CampaignAudience.SelectedSpeakers);
        request.SpeakerProfileIds = new List<int>();

        await Assert.ThrowsAsync<ArgumentException>(
            () => BuildService().SendAsync(request, "admin", "Admin"));
    }

    [Fact]
    public async Task Send_ByType_RequiresATypeFilter()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => BuildService().SendAsync(Request(CampaignAudience.SpeakersByType), "admin", "Admin"));
    }

    // ── Personalisation ──────────────────────────────────────────────────────

    [Fact]
    public async Task Send_PersonalisesEachRecipientIndividually()
    {
        // The core regression: one template, different rendered body per person.
        await BuildService().SendAsync(Request(), "admin", "Admin");

        var toAmina = _dispatcher.Sent.Single(s => s.Email.ToEmail == "amina@example.org");
        var toLuka = _dispatcher.Sent.Single(s => s.Email.ToEmail == "luka@example.org");

        Assert.Contains("Dear Amina", toAmina.Email.TextBody);
        Assert.Contains("Dear Luka", toLuka.Email.TextBody);
    }

    [Fact]
    public async Task Send_NeverLeavesLiteralPlaceholdersInAnyMessage()
    {
        await BuildService().SendAsync(
            Request(body: "Hi {{firstName}} from {{organization}} — see {{portalUrl}}."),
            "admin", "Admin");

        Assert.All(_dispatcher.Sent, s =>
        {
            Assert.DoesNotContain("{{", s.Email.HtmlBody);
            Assert.DoesNotContain("{{", s.Email.TextBody);
            Assert.DoesNotContain("{{", s.Email.Subject);
        });
    }

    [Fact]
    public async Task Send_SubstitutesTheSubjectToo()
    {
        await BuildService().SendAsync(Request(), "admin", "Admin");

        var expected = $"FLS {DateTime.UtcNow.Year}";
        Assert.All(_dispatcher.Sent, s => Assert.Equal(expected, s.Email.Subject));
    }

    [Fact]
    public async Task Send_PassesTheRequestedProviderThrough()
    {
        var request = Request();
        request.ProviderKey = "mailchimp";

        await BuildService().SendAsync(request, "admin", "Admin");

        Assert.All(_dispatcher.Sent, s => Assert.Equal("mailchimp", s.Provider));
    }

    // ── Delivery record ──────────────────────────────────────────────────────

    [Fact]
    public async Task Send_RecordsTheCampaignAndItsRecipients()
    {
        var result = await BuildService().SendAsync(Request(), "user-9", "Partnerships Team");

        var stored = await _context.EmailCampaigns
            .Include(c => c.Recipients)
            .FirstAsync(c => c.Id == result.Id);

        Assert.Equal("Partnerships Team", stored.CreatedByName);
        Assert.Equal("user-9", stored.CreatedByUserId);
        Assert.Equal(2, stored.Recipients.Count);
        Assert.All(stored.Recipients, r => Assert.Equal(EmailDeliveryStatus.Sent, r.Status));
    }

    [Fact]
    public async Task Send_MarksPartialFailureAndKeepsTheError()
    {
        _dispatcher.FailFor.Add("luka@example.org");

        var result = await BuildService().SendAsync(Request(), "admin", "Admin");

        Assert.Equal(CampaignStatus.PartiallyFailed, result.Status);
        Assert.Equal(1, result.SentCount);
        Assert.Equal(1, result.FailedCount);

        var failed = result.Recipients.Single(r => r.Status == EmailDeliveryStatus.Failed);
        Assert.Equal("luka@example.org", failed.Email);
        Assert.Contains("mailbox full", failed.Error);
    }

    [Fact]
    public async Task Send_MarksTotalFailureWhenNothingIsDelivered()
    {
        _dispatcher.FailFor.Add("amina@example.org");
        _dispatcher.FailFor.Add("luka@example.org");

        var result = await BuildService().SendAsync(Request(), "admin", "Admin");

        Assert.Equal(CampaignStatus.Failed, result.Status);
        Assert.Equal(0, result.SentCount);
    }

    [Fact]
    public async Task Send_ContinuesAfterAFailedRecipient()
    {
        // One bad address must not abort the rest of the broadcast.
        _dispatcher.FailFor.Add("amina@example.org");

        var result = await BuildService().SendAsync(Request(), "admin", "Admin");

        Assert.Equal(2, _dispatcher.Sent.Count);
        Assert.Equal(1, result.SentCount);
    }

    [Fact]
    public async Task RetryFailed_OnlyResendsTheFailures()
    {
        _dispatcher.FailFor.Add("luka@example.org");
        var service = BuildService();
        var sent = await service.SendAsync(Request(), "admin", "Admin");

        _dispatcher.Sent.Clear();
        _dispatcher.FailFor.Clear();

        var retried = await service.RetryFailedAsync(sent.Id, providerKey: null);

        var resent = Assert.Single(_dispatcher.Sent);
        Assert.Equal("luka@example.org", resent.Email.ToEmail);
        Assert.Equal(0, retried.FailedCount);
        Assert.Equal(CampaignStatus.Completed, retried.Status);
    }

    [Fact]
    public async Task Send_CreatesInAppNotifications_WhenRequested()
    {
        var request = Request();
        request.AlsoCreateInAppNotification = true;

        await BuildService().SendAsync(request, "admin", "Admin");

        Assert.Equal(2, await _context.SpeakerNotifications.CountAsync());
    }

    [Fact]
    public async Task Send_SkipsInAppNotifications_WhenNotRequested()
    {
        await BuildService().SendAsync(Request(), "admin", "Admin");

        Assert.Equal(0, await _context.SpeakerNotifications.CountAsync());
    }

    // ── Guardrails ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_RefusesAnAudienceOverTheRecipientCap()
    {
        var service = BuildService(new EmailOptions { MaxRecipientsPerCampaign = 1 });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SendAsync(Request(), "admin", "Admin"));

        Assert.Contains("limit is 1", ex.Message);
        Assert.Empty(_dispatcher.Sent);
    }

    [Fact]
    public async Task Send_RefusesAnEmptyAudience()
    {
        var request = Request(CampaignAudience.SpeakersByType);
        request.SpeakerTypeFilter = SpeakerType.Workshop; // nobody is a workshop speaker

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildService().SendAsync(request, "admin", "Admin"));
    }

    [Fact]
    public async Task Send_RequiresASubjectAndBody()
    {
        var service = BuildService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendAsync(new SendCampaignRequest { Subject = "", Body = "x" }, "a", "A"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.SendAsync(new SendCampaignRequest { Subject = "x", Body = "" }, "a", "A"));
    }

    // ── Preview ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_ReportsTheAudienceWithoutSendingAnything()
    {
        var preview = await BuildService().PreviewAsync(new PreviewCampaignRequest
        {
            Subject = "Hi", Body = "Dear {{firstName}}", Audience = CampaignAudience.ActiveSpeakers
        });

        Assert.Equal(2, preview.RecipientCount);
        Assert.Empty(_dispatcher.Sent);
        Assert.Empty(await _context.EmailCampaigns.ToListAsync());
    }

    [Fact]
    public async Task Preview_SurfacesUnresolvedPlaceholders()
    {
        var preview = await BuildService().PreviewAsync(new PreviewCampaignRequest
        {
            Subject = "Hi", Body = "Dear {{firstName}}, your {{invoiceNumber}} is ready.",
            Audience = CampaignAudience.ActiveSpeakers
        });

        Assert.Contains("invoiceNumber", preview.UnresolvedVariables);
        Assert.DoesNotContain("firstName", preview.UnresolvedVariables);
    }

    [Fact]
    public async Task Preview_WarnsWhenTheAudienceExceedsTheCap()
    {
        var service = BuildService(new EmailOptions { MaxRecipientsPerCampaign = 1 });

        var preview = await service.PreviewAsync(new PreviewCampaignRequest
        {
            Subject = "Hi", Body = "Body", Audience = CampaignAudience.ActiveSpeakers
        });

        Assert.Contains(preview.Warnings, w => w.Contains("limit is 1"));
    }

    [Fact]
    public async Task Preview_RendersAgainstARealRecipient()
    {
        var preview = await BuildService().PreviewAsync(new PreviewCampaignRequest
        {
            Subject = "Hi", Body = "Dear {{firstName}}", Audience = CampaignAudience.SpeakersWithIncompleteUploads
        });

        Assert.Contains("Dear Luka", preview.RenderedText);
        Assert.Contains("luka@example.org", preview.SampleRecipients[0]);
    }
}
