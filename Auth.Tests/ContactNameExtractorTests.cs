using Auth.Models.Enums.Mailing;
using Auth.Services.Services.Mailing;

namespace Auth.Tests;

/// <summary>
/// Tests for deriving a contact person's name from a firm's email address.
///
/// The stakes: this decides how a few hundred real firms get addressed in outreach. A wrong
/// "High" confidence result means a potential sponsor receives "Dear Info" or "Dear
/// Prodaja". The extractor is therefore biased toward returning nothing — a firm with no
/// detected name still gets a perfectly good firm-addressed email.
/// </summary>
public class ContactNameExtractorTests
{
    private readonly ContactNameExtractor _extractor = new();

    // ── Strong signal: first.last ─────────────────────────────────────────────

    [Theory]
    [InlineData("amir.hodzic@acme.ba", "Amir Hodzic")]
    [InlineData("selma_begic@acme.ba", "Selma Begic")]
    [InlineData("john-smith@acme.com", "John Smith")]
    [InlineData("Amina.Kovacevic@Acme.BA", "Amina Kovacevic")]
    public void SeparatedTokens_YieldHighConfidenceFullName(string email, string expected)
    {
        var result = _extractor.Extract(email, "Acme d.o.o.");

        Assert.Equal(expected, result.FullName);
        Assert.Equal(NameConfidence.High, result.Confidence);
        Assert.Equal(ContactNameSource.DerivedFromEmail, result.Source);
        Assert.True(result.IsUsable);
    }

    [Fact]
    public void TrailingDigits_AreStrippedFromTheName()
    {
        var result = _extractor.Extract("amir.hodzic2@acme.ba");

        Assert.Equal("Amir Hodzic", result.FullName);
        Assert.Equal(NameConfidence.High, result.Confidence);
    }

    [Fact]
    public void MiddleToken_IsDroppedInFavourOfFirstAndLast()
    {
        var result = _extractor.Extract("amir.b.hodzic@acme.ba");

        Assert.Equal("Amir Hodzic", result.FullName);
    }

    [Fact]
    public void HyphenatedSurname_KeepsItsInternalCapital()
    {
        var result = _extractor.Extract("amina.hadzi-omerovic@acme.ba");

        Assert.Equal("Amina Hadzi-Omerovic", result.FullName);
    }

    // ── Generic mailboxes must never produce a name ───────────────────────────

    [Theory]
    [InlineData("info@acme.ba")]
    [InlineData("contact@acme.ba")]
    [InlineData("office@acme.ba")]
    [InlineData("sales@acme.ba")]
    [InlineData("hr@acme.ba")]
    [InlineData("noreply@acme.ba")]
    [InlineData("no-reply@acme.ba")]
    public void EnglishGenericMailboxes_YieldNoName(string email)
    {
        var result = _extractor.Extract(email, "Acme d.o.o.");

        Assert.False(result.HasName);
        Assert.False(result.IsUsable);
        Assert.Equal(NameConfidence.None, result.Confidence);
        Assert.True(_extractor.IsGenericMailbox(email));
    }

    [Theory]
    [InlineData("kontakt@acme.ba")]
    [InlineData("uprava@acme.ba")]
    [InlineData("direkcija@acme.ba")]
    [InlineData("prodaja@acme.ba")]
    [InlineData("racunovodstvo@acme.ba")]
    [InlineData("ljudskiresursi@acme.ba")]
    [InlineData("recepcija@acme.ba")]
    [InlineData("nabavka@acme.ba")]
    public void BosnianGenericMailboxes_YieldNoName(string email)
    {
        // A BH firm directory is full of these. An English-only exclusion list would happily
        // address a bank's management inbox as "Dear Uprava".
        var result = _extractor.Extract(email, "Acme d.o.o.");

        Assert.False(result.HasName);
        Assert.True(_extractor.IsGenericMailbox(email));
    }

    [Theory]
    [InlineData("info.sarajevo@acme.ba")]
    [InlineData("prodaja-bl@acme.ba")]
    [InlineData("hr_team@acme.ba")]
    public void GenericMailboxWithQualifier_IsStillGeneric(string email)
    {
        // "info.sarajevo" has two separated tokens and would otherwise score High.
        var result = _extractor.Extract(email);

        Assert.False(result.HasName);
        Assert.True(_extractor.IsGenericMailbox(email));
    }

    // ── Mailbox that just repeats the company ─────────────────────────────────

    [Fact]
    public void MailboxMatchingTheDomain_IsNotAPerson()
    {
        var result = _extractor.Extract("acme@acme.ba", "Acme");

        Assert.False(result.HasName);
        Assert.Contains("repeats the company", result.Reason ?? string.Empty);
    }

    [Fact]
    public void MailboxMatchingTheFirmName_IsNotAPerson()
    {
        var result = _extractor.Extract("sarajevoosiguranje@mail.ba", "Sarajevo Osiguranje");

        Assert.False(result.HasName);
    }

    // ── Weak signals fall back rather than guessing ───────────────────────────

    [Fact]
    public void InitialAndSurname_IsRecordedButNotUsable()
    {
        // We have a surname but no given name. "Dear A. Hodzic" reads badly, so this is
        // stored for a human to correct and falls back to the firm-addressed variant.
        var result = _extractor.Extract("a.hodzic@acme.ba");

        Assert.Equal("A. Hodzic", result.FullName);
        Assert.Equal(NameConfidence.Low, result.Confidence);
        Assert.False(result.IsUsable);
    }

    [Fact]
    public void SingleGivenName_IsUsableAtMediumConfidence()
    {
        var result = _extractor.Extract("amina@acme.ba");

        Assert.Equal("Amina", result.FullName);
        Assert.Equal(NameConfidence.Medium, result.Confidence);
        Assert.True(result.IsUsable);
    }

    [Fact]
    public void RunTogetherName_IsNotUsable()
    {
        // "amirhodzic" — there is no reliable way to know where the split belongs.
        var result = _extractor.Extract("amirhodzicsarajevo@acme.ba");

        Assert.Equal(NameConfidence.Low, result.Confidence);
        Assert.False(result.IsUsable);
    }

    // ── Firm-name fallback ────────────────────────────────────────────────────

    [Fact]
    public void FirmNameWithProfessionalTitle_YieldsAName()
    {
        var result = _extractor.Extract(null, "Advokat Amir Hodzic");

        Assert.Equal("Amir Hodzic", result.FullName);
        Assert.Equal(ContactNameSource.DerivedFromFirmName, result.Source);
        Assert.Equal(NameConfidence.Medium, result.Confidence);
    }

    [Fact]
    public void FirmNameWithoutTitle_YieldsNothing()
    {
        // "Acme Trading" — no way to tell a surname from a brand, so don't guess.
        var result = _extractor.Extract(null, "Acme Trading");

        Assert.False(result.HasName);
    }

    [Fact]
    public void GenericMailboxFallsBackToTheFirmName()
    {
        var result = _extractor.Extract("info@advokat-hodzic.ba", "Advokat Amir Hodzic");

        Assert.Equal("Amir Hodzic", result.FullName);
        Assert.Equal(ContactNameSource.DerivedFromFirmName, result.Source);
    }

    [Fact]
    public void LegalFormTokens_DoNotBecomeNameParts()
    {
        var result = _extractor.Extract(null, "Advokat Hodzic d.o.o.");

        Assert.Equal("Hodzic", result.FullName);
        // One name part after a title is a guess, not a confident read.
        Assert.Equal(NameConfidence.Low, result.Confidence);
        Assert.False(result.IsUsable);
    }

    // ── Degenerate input ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData("not-an-email", null)]
    [InlineData("@acme.ba", null)]
    [InlineData("123456@acme.ba", null)]
    public void DegenerateInput_NeverThrowsAndNeverInvents(string? email, string? firmName)
    {
        var result = _extractor.Extract(email, firmName);

        Assert.False(result.IsUsable);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void EveryResultCarriesAnExplanation()
    {
        // The bulk-detect review table shows Reason to the user, so a blank result must say
        // why rather than appearing as an unexplained empty cell.
        var cases = new[] { "info@acme.ba", "amir.hodzic@acme.ba", "amina@acme.ba", "a.b@acme.ba" };

        foreach (var email in cases)
        {
            var result = _extractor.Extract(email, "Acme d.o.o.");
            Assert.False(string.IsNullOrWhiteSpace(result.Reason), $"No reason given for {email}");
        }
    }
}
