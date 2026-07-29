using Auth.Models.Entities;
using Auth.Services.Services;

namespace Auth.Tests;

/// <summary>
/// Regression tests for the program-manager journal overview.
///
/// The bug: the overview grid inferred "submitted" from the presence of a satisfaction
/// answer, while the per-scholar detail page read the JournalSubmissions table. The two
/// screens disagreed — a scholar who submitted without answering the satisfaction
/// question showed a red X in the overview and a green tick in the detail view, and a
/// month with a score but no submission record showed the reverse.
/// </summary>
public class JournalOverviewTests
{
    private static Answer Score(string month, string response) => new()
    {
        ScholarId = "scholar-1",
        MonthYear = month,
        Response = response
    };

    private static JournalSubmission Submitted(string month, bool submitted = true) => new()
    {
        ScholarId = "scholar-1",
        MonthYear = month,
        Submitted = submitted
    };

    [Fact]
    public void BuildSubmissions_MarksAMonthSubmitted_EvenWithNoSatisfactionAnswer()
    {
        var result = ManagerService.BuildSubmissions(
            satisfactionAnswers: new List<Answer>(),
            submissions: new List<JournalSubmission> { Submitted("2026-03") });

        var month = Assert.Single(result);
        Assert.Equal("2026-03", month.MonthYear);
        Assert.True(month.Submitted);
        Assert.Null(month.SatisfactionScore);
    }

    [Fact]
    public void BuildSubmissions_DoesNotMarkSubmitted_WhenOnlyAScoreExists()
    {
        // An answer saved as a draft is not a submission.
        var result = ManagerService.BuildSubmissions(
            satisfactionAnswers: new List<Answer> { Score("2026-03", "8") },
            submissions: new List<JournalSubmission>());

        var month = Assert.Single(result);
        Assert.False(month.Submitted);
        Assert.Equal(80, month.SatisfactionScore);
    }

    [Fact]
    public void BuildSubmissions_RespectsAFalseSubmittedFlag()
    {
        var result = ManagerService.BuildSubmissions(
            satisfactionAnswers: new List<Answer> { Score("2026-03", "9") },
            submissions: new List<JournalSubmission> { Submitted("2026-03", submitted: false) });

        var month = Assert.Single(result);
        Assert.False(month.Submitted);
    }

    [Fact]
    public void BuildSubmissions_MergesBothSources()
    {
        var result = ManagerService.BuildSubmissions(
            satisfactionAnswers: new List<Answer> { Score("2026-01", "7"), Score("2026-02", "9") },
            submissions: new List<JournalSubmission> { Submitted("2026-02"), Submitted("2026-03") });

        Assert.Equal(3, result.Count);

        var january = result.Single(m => m.MonthYear == "2026-01");
        Assert.False(january.Submitted);
        Assert.Equal(70, january.SatisfactionScore);

        var february = result.Single(m => m.MonthYear == "2026-02");
        Assert.True(february.Submitted);
        Assert.Equal(90, february.SatisfactionScore);

        var march = result.Single(m => m.MonthYear == "2026-03");
        Assert.True(march.Submitted);
        Assert.Null(march.SatisfactionScore);
    }

    [Fact]
    public void BuildSubmissions_ScalesScoresToPercent()
    {
        var result = ManagerService.BuildSubmissions(
            new List<Answer> { Score("2026-04", "10") },
            new List<JournalSubmission>());

        Assert.Equal(100, Assert.Single(result).SatisfactionScore);
    }

    [Fact]
    public void BuildSubmissions_IgnoresUnparseableResponses()
    {
        // Free-text in a numeric slot must not be scored as zero — zero reads as
        // "rated it terribly", which is a different claim from "didn't answer".
        var result = ManagerService.BuildSubmissions(
            new List<Answer> { Score("2026-05", "not a number") },
            new List<JournalSubmission>());

        Assert.Null(Assert.Single(result).SatisfactionScore);
    }

    [Fact]
    public void BuildSubmissions_AveragesMultipleAnswersInAMonth()
    {
        var result = ManagerService.BuildSubmissions(
            new List<Answer> { Score("2026-06", "6"), Score("2026-06", "8") },
            new List<JournalSubmission>());

        Assert.Equal(70, Assert.Single(result).SatisfactionScore);
    }

    [Fact]
    public void BuildSubmissions_TreatsAMonthAsSubmitted_IfAnyDuplicateRowSaysSo()
    {
        // Older data can contain more than one row per scholar/month.
        var result = ManagerService.BuildSubmissions(
            new List<Answer>(),
            new List<JournalSubmission>
            {
                Submitted("2026-07", submitted: false),
                Submitted("2026-07", submitted: true)
            });

        Assert.True(Assert.Single(result).Submitted);
    }

    [Fact]
    public void BuildSubmissions_ReturnsMonthsNewestFirst()
    {
        var result = ManagerService.BuildSubmissions(
            new List<Answer>(),
            new List<JournalSubmission> { Submitted("2026-01"), Submitted("2026-11"), Submitted("2026-05") });

        Assert.Equal(new[] { "2026-11", "2026-05", "2026-01" }, result.Select(m => m.MonthYear));
    }

    [Fact]
    public void BuildSubmissions_HandlesNullInputs()
    {
        var result = ManagerService.BuildSubmissions(null, null);
        Assert.Empty(result);
    }
}
