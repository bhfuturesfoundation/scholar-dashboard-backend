using Auth.Services.Services.Email;

namespace Auth.Tests;

/// <summary>
/// Regression tests for template rendering.
///
/// The bug these guard against: campaign templates used {{firstName}} placeholders that
/// nothing ever substituted, so a broadcast went out reading "Dear {{firstName}}". A
/// second, quieter bug was that recipient data was interpolated straight into an HTML
/// attribute with no escaping.
/// </summary>
public class EmailTemplateRendererTests
{
    private readonly EmailTemplateRenderer _renderer = new();

    private static Dictionary<string, string?> Vars(params (string Key, string? Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Render_SubstitutesPlaceholders_InSubjectAndBody()
    {
        var result = _renderer.Render(
            "Welcome {{firstName}}",
            "Dear {{firstName}} {{lastName}}, see you in {{year}}.",
            Vars(("firstName", "Amina"), ("lastName", "Hodzic"), ("year", "2026")));

        Assert.Equal("Welcome Amina", result.Subject);
        Assert.Contains("Dear Amina Hodzic, see you in 2026.", result.TextBody);
        Assert.Contains("Dear Amina Hodzic", result.HtmlBody);
    }

    [Fact]
    public void Render_NeverLeavesLiteralPlaceholdersInOutput()
    {
        // The headline regression: no rendered output may contain "{{".
        var result = _renderer.Render(
            "Hello {{firstName}}",
            "Dear {{firstName}}, your {{unknownThing}} is ready.",
            Vars(("firstName", "Amina")));

        Assert.DoesNotContain("{{", result.Subject);
        Assert.DoesNotContain("{{", result.TextBody);
        Assert.DoesNotContain("{{", result.HtmlBody);
    }

    [Fact]
    public void Render_ReportsUnresolvedPlaceholders()
    {
        var result = _renderer.Render(
            "Hi {{firstName}}",
            "Your {{missingOne}} and {{missingTwo}} are pending.",
            Vars(("firstName", "Amina")));

        Assert.Contains("missingOne", result.UnresolvedVariables);
        Assert.Contains("missingTwo", result.UnresolvedVariables);
        Assert.DoesNotContain("firstName", result.UnresolvedVariables);
    }

    [Fact]
    public void Render_MatchesPlaceholderNamesCaseInsensitively()
    {
        // Partner members type these by hand; casing shouldn't decide whether it works.
        var result = _renderer.Render(
            "x",
            "{{FirstName}} / {{firstname}} / {{ firstName }}",
            Vars(("firstName", "Amina")));

        Assert.Contains("Amina / Amina / Amina", result.TextBody);
        Assert.Empty(result.UnresolvedVariables);
    }

    [Fact]
    public void Render_EscapesHtmlInSubstitutedValues()
    {
        // A speaker's organisation containing markup must not reach the HTML body raw.
        var result = _renderer.Render(
            "x",
            "From {{organization}}.",
            Vars(("organization", "<script>alert('xss')</script>")));

        Assert.DoesNotContain("<script>", result.HtmlBody);
        Assert.Contains("&lt;script&gt;", result.HtmlBody);

        // The plain-text alternative keeps the raw value — it is never parsed as markup.
        Assert.Contains("<script>", result.TextBody);
    }

    [Fact]
    public void Render_EscapesHtmlPresentInTheTemplateItself()
    {
        var result = _renderer.Render("x", "Costs < 100 & rising", Vars());

        Assert.Contains("&lt; 100 &amp; rising", result.HtmlBody);
        Assert.DoesNotContain("< 100 & rising", result.HtmlBody);
    }

    [Fact]
    public void Render_DoesNotReExpandPlaceholdersComingFromData()
    {
        // A value that itself looks like a placeholder must be treated as literal text,
        // otherwise data could inject a second substitution pass.
        var result = _renderer.Render(
            "x",
            "Hello {{firstName}}",
            Vars(("firstName", "{{lastName}}"), ("lastName", "SHOULD-NOT-APPEAR")));

        Assert.DoesNotContain("SHOULD-NOT-APPEAR", result.TextBody);
        Assert.Contains("{{lastName}}", result.TextBody);
    }

    [Fact]
    public void Render_ConvertsBlankLinesToParagraphs()
    {
        var result = _renderer.Render("x", "First para.\n\nSecond para.", Vars());

        Assert.Contains("<p style=\"margin:0 0 16px 0;\">First para.</p>", result.HtmlBody);
        Assert.Contains("<p style=\"margin:0 0 16px 0;\">Second para.</p>", result.HtmlBody);
    }

    [Fact]
    public void Render_ConvertsSingleNewlinesToLineBreaks()
    {
        var result = _renderer.Render("x", "Line one\nLine two", Vars());

        Assert.Contains("Line one<br />Line two", result.HtmlBody);
    }

    [Fact]
    public void Render_LinkifiesBareUrls()
    {
        var result = _renderer.Render("x", "Sign in at https://fls.ba/portal today.", Vars());

        Assert.Contains("<a href=\"https://fls.ba/portal\"", result.HtmlBody);
    }

    [Fact]
    public void Render_ProducesACompleteHtmlDocument()
    {
        var result = _renderer.Render("Subject", "Body", Vars());

        Assert.StartsWith("<!DOCTYPE html>", result.HtmlBody);
        Assert.Contains("Future Leaders Summit", result.HtmlBody);
        Assert.Contains("</html>", result.HtmlBody);
    }

    [Fact]
    public void Render_TrimsTheSubject()
    {
        var result = _renderer.Render("  Padded  ", "Body", Vars());
        Assert.Equal("Padded", result.Subject);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Render_HandlesEmptyBodyWithoutThrowing(string? body)
    {
        var result = _renderer.Render("Subject", body!, Vars());

        Assert.Equal("Subject", result.Subject);
        Assert.Equal(string.Empty, result.TextBody);
    }

    [Fact]
    public void ExtractVariableNames_ReturnsDistinctNames()
    {
        var names = _renderer.ExtractVariableNames(
            "{{firstName}} {{lastName}} {{firstName}} {{ deadline }}");

        Assert.Equal(3, names.Count);
        Assert.Contains("firstName", names);
        Assert.Contains("lastName", names);
        Assert.Contains("deadline", names);
    }

    [Fact]
    public void ExtractVariableNames_IgnoresMalformedPlaceholders()
    {
        var names = _renderer.ExtractVariableNames("{firstName} {{ }} {{123abc}} {{valid}}");

        Assert.Single(names);
        Assert.Contains("valid", names);
    }
}
