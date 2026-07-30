using Auth.Models.Entities.Mailing;
using Auth.Services.Services.Mailing;

namespace Auth.Tests;

/// <summary>
/// Tests for classifying a firm into a type from its name, website and email domain.
///
/// The load-bearing behaviour is diacritic folding: Bosnian firm names are written
/// "Štedionica" and "Računovodstvo" while their domains and the team's typed keywords are
/// "stedionica" and "racunovodstvo". Without folding, keyword matching misses most local
/// firms — which would make the whole auto-categorisation feature useless in this market.
/// </summary>
public class FirmCategorizerTests
{
    private readonly FirmCategorizer _categorizer = new();

    private static List<FirmType> Types() => new()
    {
        new FirmType { Id = 1, Name = "Bank", Slug = "bank", MatchKeywords = "bank,banka,stedionica" },
        // "odvjetni" rather than "odvjetnik": the adjectival form is "odvjetničko", which
        // shares only the stem. The seeded keywords use stems for the same reason.
        new FirmType { Id = 2, Name = "Law firm", Slug = "law-firm", MatchKeywords = "law,advokat,odvjetni,notar" },
        new FirmType { Id = 3, Name = "IT company", Slug = "it-company", MatchKeywords = "software,tech,it,digital" },
        new FirmType { Id = 4, Name = "Hospital", Slug = "hospital", MatchKeywords = "hospital,bolnica,klinika" },
        new FirmType { Id = 5, Name = "Investment bank", Slug = "investment-bank", MatchKeywords = "investment bank" },
    };

    [Theory]
    [InlineData("Raiffeisen Bank", 1)]
    [InlineData("ASA Banka d.d.", 1)]
    [InlineData("Advokatska kancelarija Hodzic", 2)]
    [InlineData("Odvjetnicko drustvo Maric", 2)]
    [InlineData("Sarajevo Software Solutions", 3)]
    [InlineData("Opsta bolnica Konjic", 4)]
    public void MatchesKeywordInFirmName(string firmName, int expectedTypeId)
    {
        var result = _categorizer.Suggest(firmName, null, null, Types());

        Assert.Equal(expectedTypeId, result.FirmTypeId);
        Assert.True(result.IsConfident);
    }

    [Fact]
    public void MatchesDespiteDiacritics()
    {
        // "Štedionica" folds to "stedionica", which is how the keyword is stored.
        var result = _categorizer.Suggest("Prva Štedionica d.d.", null, null, Types());

        Assert.Equal(1, result.FirmTypeId);
    }

    [Fact]
    public void MatchesOnEmailDomainWhenTheNameDoesNot()
    {
        // The trading name gives nothing away; the domain does.
        var result = _categorizer.Suggest("Prima Group", null, "info@primabanka.ba", Types());

        Assert.Equal(1, result.FirmTypeId);
    }

    [Fact]
    public void MatchesOnWebsite()
    {
        var result = _categorizer.Suggest("Prima Group", "https://prima-software.ba", null, Types());

        Assert.Equal(3, result.FirmTypeId);
    }

    [Fact]
    public void LongerKeywordWinsOverShorter()
    {
        // "investment bank" must beat "bank" — the more specific classification is correct.
        var result = _categorizer.Suggest("Balkan Investment Bank", null, null, Types());

        Assert.Equal(5, result.FirmTypeId);
    }

    [Fact]
    public void SubstringMatchInsideAWordIsNotConfident()
    {
        // "it" appears inside "Digitalno" and countless other words. A mid-word hit is a
        // guess, and bulk categorisation only auto-applies confident ones.
        var types = new List<FirmType>
        {
            new() { Id = 1, Name = "IT company", Slug = "it", MatchKeywords = "it" }
        };

        var result = _categorizer.Suggest("Kreditna Unija", null, null, types);

        if (result.HasSuggestion) Assert.False(result.IsConfident);
    }

    [Fact]
    public void NoKeywordMatch_ReturnsNoSuggestion()
    {
        var result = _categorizer.Suggest("Zzyzx Holdings", null, null, Types());

        Assert.False(result.HasSuggestion);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void TypesWithoutKeywords_AreNeverSuggested()
    {
        // A type seeded without keywords classifies nothing — worth pinning so an empty
        // MatchKeywords never silently becomes a catch-all.
        var types = new List<FirmType>
        {
            new() { Id = 1, Name = "Uncategorised", Slug = "uncategorised", MatchKeywords = null }
        };

        var result = _categorizer.Suggest("Raiffeisen Bank", null, null, types);

        Assert.False(result.HasSuggestion);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData("   ", null, null)]
    public void DegenerateInput_ReturnsNoSuggestionWithoutThrowing(string? name, string? site, string? email)
    {
        var result = _categorizer.Suggest(name, site, email, Types());

        Assert.False(result.HasSuggestion);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void ReportsWhichKeywordMatched()
    {
        // Shown in the review table so the operator can correct the keyword, not just the
        // classification.
        var result = _categorizer.Suggest("Raiffeisen Bank", null, null, Types());

        Assert.Equal("bank", result.MatchedKeyword);
        Assert.Contains("bank", result.Reason!, StringComparison.OrdinalIgnoreCase);
    }
}
