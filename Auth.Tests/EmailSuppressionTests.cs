using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Entities.Email;
using Auth.Models.Entities.Mailing;
using Auth.Models.Enums.Mailing;
using Auth.Services.Interfaces.Email;
using Auth.Services.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth.Tests;

/// <summary>
/// Tests for the final gate before any email leaves the system.
///
/// The requirement: a deactivated scholar receives nothing. The design decision worth
/// pinning is WHERE that is enforced — inside the dispatcher rather than in each audience
/// query. There are many audience queries, they are written by hand, and one of them
/// forgetting to exclude deactivated accounts is a matter of time. These tests assert that
/// forgetting is harmless.
/// </summary>
public class EmailSuppressionTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly EmailSuppressionService _service;

    public EmailSuppressionTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"suppression-{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _service = new EmailSuppressionService(_context, NullLogger<EmailSuppressionService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    private async Task AddUserAsync(string email, bool isActive)
    {
        _context.Users.Add(new User
        {
            Id = Guid.NewGuid().ToString(),
            Email = email,
            UserName = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FirstName = "Test",
            LastName = "Scholar",
            IsActive = isActive
        });
        await _context.SaveChangesAsync();
    }

    private async Task AddFirmAsync(string email, FirmStatus status)
    {
        _context.Firms.Add(new Firm
        {
            Name = "Acme d.o.o.",
            Email = email,
            NormalizedEmail = email.ToLowerInvariant(),
            Status = status
        });
        await _context.SaveChangesAsync();
    }

    // ── The headline requirement ──────────────────────────────────────────────

    [Fact]
    public async Task DeactivatedScholar_IsSuppressed()
    {
        await AddUserAsync("inactive@bhff.org", isActive: false);

        var check = await _service.CheckAsync("inactive@bhff.org");

        Assert.True(check.IsSuppressed);
        Assert.Equal(SuppressionReason.UserInactive, check.Reason);
    }

    [Fact]
    public async Task ActiveScholar_IsAllowed()
    {
        await AddUserAsync("active@bhff.org", isActive: true);

        var check = await _service.CheckAsync("active@bhff.org");

        Assert.False(check.IsSuppressed);
    }

    [Fact]
    public async Task SuppressionIsCaseAndWhitespaceInsensitive()
    {
        // A CSV import or a hand-typed audience will not match the stored casing, and an
        // address that slips through on casing alone defeats the whole mechanism.
        await AddUserAsync("inactive@bhff.org", isActive: false);

        foreach (var variant in new[] { "INACTIVE@BHFF.ORG", "  Inactive@BHFF.org  ", "InAcTiVe@bhff.org" })
        {
            var check = await _service.CheckAsync(variant);
            Assert.True(check.IsSuppressed, $"Variant not suppressed: '{variant}'");
        }
    }

    // ── Firms ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FirmStatus.Unsubscribed, SuppressionReason.FirmUnsubscribed)]
    [InlineData(FirmStatus.Bounced, SuppressionReason.FirmBounced)]
    [InlineData(FirmStatus.DoNotContact, SuppressionReason.FirmDoNotContact)]
    [InlineData(FirmStatus.Incomplete, SuppressionReason.InvalidAddress)]
    public async Task NonContactableFirm_IsSuppressedWithTheRightReason(
        FirmStatus status, SuppressionReason expected)
    {
        await AddFirmAsync("firm@acme.ba", status);

        var check = await _service.CheckAsync("firm@acme.ba");

        Assert.True(check.IsSuppressed);
        Assert.Equal(expected, check.Reason);
    }

    [Fact]
    public async Task ActiveFirm_IsAllowed()
    {
        await AddFirmAsync("firm@acme.ba", FirmStatus.Active);

        var check = await _service.CheckAsync("firm@acme.ba");

        Assert.False(check.IsSuppressed);
    }

    // ── Explicit suppression list ─────────────────────────────────────────────

    [Fact]
    public async Task SuppressionListEntry_OutranksAnActiveUserRecord()
    {
        // Someone who unsubscribed but still has an active account must stay suppressed.
        await AddUserAsync("optout@bhff.org", isActive: true);
        _context.EmailSuppressions.Add(new EmailSuppression
        {
            NormalizedEmail = "optout@bhff.org",
            Reason = "Unsubscribed via link"
        });
        await _context.SaveChangesAsync();

        var check = await _service.CheckAsync("optout@bhff.org");

        Assert.True(check.IsSuppressed);
        Assert.Equal(SuppressionReason.ManuallySuppressed, check.Reason);
    }

    [Fact]
    public async Task LiftedSuppression_NoLongerBlocks()
    {
        await AddUserAsync("back@bhff.org", isActive: true);
        _context.EmailSuppressions.Add(new EmailSuppression
        {
            NormalizedEmail = "back@bhff.org",
            Reason = "Unsubscribed",
            LiftedAt = DateTime.UtcNow.AddDays(-1)
        });
        await _context.SaveChangesAsync();

        var check = await _service.CheckAsync("back@bhff.org");

        Assert.False(check.IsSuppressed);
    }

    // ── Degenerate input ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankAddress_IsSuppressedAsInvalid(string? email)
    {
        var check = await _service.CheckAsync(email);

        Assert.True(check.IsSuppressed);
        Assert.Equal(SuppressionReason.InvalidAddress, check.Reason);
    }

    [Fact]
    public async Task UnknownAddress_IsAllowed()
    {
        // Not every recipient is a user or a firm — password resets go to addresses with no
        // firm record. Absence of evidence must not mean suppression.
        var check = await _service.CheckAsync("stranger@example.com");

        Assert.False(check.IsSuppressed);
    }

    // ── Batch form ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckMany_ReturnsOnlyTheSuppressedAddresses()
    {
        await AddUserAsync("active@bhff.org", isActive: true);
        await AddUserAsync("inactive@bhff.org", isActive: false);
        await AddFirmAsync("bounced@acme.ba", FirmStatus.Bounced);

        var results = await _service.CheckManyAsync(new[]
        {
            "active@bhff.org", "inactive@bhff.org", "bounced@acme.ba", "unknown@example.com"
        });

        Assert.Equal(2, results.Count);
        Assert.True(results.ContainsKey("inactive@bhff.org"));
        Assert.True(results.ContainsKey("bounced@acme.ba"));
        Assert.False(results.ContainsKey("active@bhff.org"));
    }

    [Fact]
    public async Task CheckMany_HandlesDuplicatesAndBlanks()
    {
        await AddUserAsync("inactive@bhff.org", isActive: false);

        var results = await _service.CheckManyAsync(new[]
        {
            "inactive@bhff.org", "INACTIVE@bhff.org", "  inactive@bhff.org  ", "", "   "
        });

        Assert.Single(results);
    }
}
