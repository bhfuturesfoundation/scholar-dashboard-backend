using Auth.Services.Services.Mailing;

namespace Auth.Tests;

/// <summary>
/// Tests for the spreadsheet header mapping.
///
/// This is the least glamorous part of the mailing module and the one most able to lose data
/// quietly. <see cref="SpreadsheetReader.BuildColumnMap"/> matches an exact folded header against
/// the alias list — not a substring — so a header the table has never seen simply produces no
/// mapping, and the import proceeds cheerfully with that column blank.
///
/// That is not hypothetical: the partnerships team's own export is headed "Ime kompanije" and
/// "Tip kompanije", neither of which was listed. Importing it produced 569 firms with no name and
/// no type, and nothing anywhere reported a problem.
/// </summary>
public class FirmImportMappingTests
{
    /// <summary>The exact header row of the team's company sheet.</summary>
    private static readonly List<string> RealHeaders =
        new() { "Ime kompanije", "Tip kompanije", "Oslovljavanje", "Kontakt email", "CC" };

    [Fact]
    public void RealTeamSpreadsheet_MapsEveryColumn()
    {
        var map = SpreadsheetReader.BuildColumnMap(RealHeaders, FirmImportExportService.ColumnAliases);

        Assert.Equal(0, map["name"]);
        Assert.Equal(1, map["type"]);
        Assert.Equal(2, map["salutation"]);
        Assert.Equal(3, map["email"]);
        Assert.Equal(4, map["cc"]);
    }

    /// <summary>
    /// The importer refuses a file with neither a name nor an email column. That guard is the only
    /// thing standing between a mis-headed file and a directory full of blank rows, so the headers
    /// it depends on must map.
    /// </summary>
    [Fact]
    public void RealTeamSpreadsheet_SatisfiesTheImportGuard()
    {
        var map = SpreadsheetReader.BuildColumnMap(RealHeaders, FirmImportExportService.ColumnAliases);

        Assert.True(map.ContainsKey("name") || map.ContainsKey("email"));
    }

    [Theory]
    [InlineData("Ime kompanije", "name")]
    [InlineData("Naziv kompanije", "name")]
    [InlineData("Company Name", "name")]
    [InlineData("Tip kompanije", "type")]
    [InlineData("Industry", "type")]
    [InlineData("Kontakt email", "email")]
    [InlineData("E-mail address", "email")]
    [InlineData("Oslovljavanje", "salutation")]
    [InlineData("Greeting", "salutation")]
    [InlineData("CC", "cc")]
    [InlineData("Kopija", "cc")]
    public void KnownHeaders_MapToTheirField(string header, string expectedField)
    {
        var map = SpreadsheetReader.BuildColumnMap(new List<string> { header }, FirmImportExportService.ColumnAliases);

        Assert.True(map.ContainsKey(expectedField), $"'{header}' did not map to '{expectedField}'.");
        Assert.Equal(0, map[expectedField]);
    }

    /// <summary>Header matching must not care about case, accents or punctuation.</summary>
    [Fact]
    public void HeaderMatching_IgnoresCaseAndPunctuation()
    {
        var variants = new List<string> { "  IME KOMPANIJE  ", "kontakt-email", "Tip_Kompanije" };
        var map = SpreadsheetReader.BuildColumnMap(variants, FirmImportExportService.ColumnAliases);

        Assert.True(map.ContainsKey("name"));
        Assert.True(map.ContainsKey("email"));
        Assert.True(map.ContainsKey("type"));
    }

    /// <summary>
    /// A header nobody has taught it about maps to nothing rather than guessing. Guessing would be
    /// worse: a wrong column silently writes the wrong data into a live outreach list.
    /// </summary>
    [Fact]
    public void UnknownHeader_MapsToNothing()
    {
        var map = SpreadsheetReader.BuildColumnMap(
            new List<string> { "Broj zaposlenih" }, FirmImportExportService.ColumnAliases);

        Assert.Empty(map);
    }

    /// <summary>
    /// Every alias must already be in folded form, or it can never match: the header is folded
    /// before comparison but the alias is not. An entry like "Ime Kompanije" would sit in the
    /// table looking correct and match nothing at all.
    /// </summary>
    [Fact]
    public void EveryAlias_IsStoredInFoldedForm()
    {
        foreach (var (field, aliases) in FirmImportExportService.ColumnAliases)
        {
            foreach (var alias in aliases)
            {
                Assert.True(
                    alias == TextNormalizer.FoldToWords(alias),
                    $"Alias '{alias}' for field '{field}' is not in folded form and can never match.");
            }
        }
    }

    /// <summary>No alias may serve two fields, or which one wins depends on dictionary order.</summary>
    [Fact]
    public void NoAlias_IsClaimedByTwoFields()
    {
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (field, aliases) in FirmImportExportService.ColumnAliases)
        {
            foreach (var alias in aliases)
            {
                Assert.False(
                    owner.TryGetValue(alias, out var existing),
                    $"Alias '{alias}' is claimed by both '{existing}' and '{field}'.");

                owner[alias] = field;
            }
        }
    }
}
