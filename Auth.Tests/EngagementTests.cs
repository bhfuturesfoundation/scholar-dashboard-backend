using Auth.Services.Services.Engagement;
using System.Reflection;

namespace Auth.Tests;

/// <summary>
/// Tests for streak arithmetic, the badge catalogue and kudos rules.
///
/// Streak logic is the part most likely to be quietly wrong: month arithmetic across a year
/// boundary, and the question of when a streak has actually ended. A streak shown to a
/// scholar is a promise about their own record, so it has to be right.
/// </summary>
public class EngagementTests
{
    // The streak helpers are private implementation detail of the service, but they are pure
    // functions over a list of dates and are exactly what needs pinning. Reflection keeps
    // them private without leaving them untested.
    private static int LongestStreak(List<DateTime> months) =>
        (int)typeof(ScholarProgressService)
            .GetMethod("LongestStreak", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { months })!;

    private static int CurrentStreak(List<DateTime> months) =>
        (int)typeof(ScholarProgressService)
            .GetMethod("CurrentStreak", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { months })!;

    private static List<DateTime> Months(params string[] values) =>
        values.Select(v => DateTime.ParseExact($"{v}-01", "yyyy-MM-dd", null)).OrderBy(d => d).ToList();

    // ── Longest streak ────────────────────────────────────────────────────────

    [Fact]
    public void LongestStreak_OfNothingIsZero()
    {
        Assert.Equal(0, LongestStreak(new List<DateTime>()));
    }

    [Fact]
    public void LongestStreak_OfOneMonthIsOne()
    {
        Assert.Equal(1, LongestStreak(Months("2026-03")));
    }

    [Fact]
    public void LongestStreak_CountsConsecutiveMonths()
    {
        Assert.Equal(4, LongestStreak(Months("2026-01", "2026-02", "2026-03", "2026-04")));
    }

    [Fact]
    public void LongestStreak_CrossesAYearBoundary()
    {
        // December to January is a gap of 1, not -11. Naive month subtraction gets this
        // wrong and silently breaks every streak spanning New Year.
        Assert.Equal(3, LongestStreak(Months("2025-11", "2025-12", "2026-01")));
    }

    [Fact]
    public void LongestStreak_ResetsOnAGap()
    {
        Assert.Equal(2, LongestStreak(Months("2026-01", "2026-02", "2026-05", "2026-06")));
    }

    [Fact]
    public void LongestStreak_ReturnsTheLongestRunNotTheLast()
    {
        Assert.Equal(3, LongestStreak(Months("2026-01", "2026-02", "2026-03", "2026-07", "2026-08")));
    }

    [Fact]
    public void LongestStreak_IgnoresDuplicateMonths()
    {
        // A duplicated month neither extends nor breaks a run.
        var months = Months("2026-01", "2026-02", "2026-02", "2026-03");

        Assert.Equal(3, LongestStreak(months));
    }

    // ── Current streak ────────────────────────────────────────────────────────

    [Fact]
    public void CurrentStreak_CountsBackFromTheLatestSubmission()
    {
        var now = DateTime.UtcNow;
        var months = Months(
            $"{now.AddMonths(-2):yyyy-MM}",
            $"{now.AddMonths(-1):yyyy-MM}",
            $"{now:yyyy-MM}");

        Assert.Equal(3, CurrentStreak(months));
    }

    [Fact]
    public void CurrentStreak_SurvivesNotHavingSubmittedThisMonthYet()
    {
        // The key behaviour: on the 1st, a scholar who hasn't filled in the current month
        // must not see their streak collapse to zero. It breaks only when a month is skipped.
        var now = DateTime.UtcNow;
        var months = Months(
            $"{now.AddMonths(-2):yyyy-MM}",
            $"{now.AddMonths(-1):yyyy-MM}");

        Assert.Equal(2, CurrentStreak(months));
    }

    [Fact]
    public void CurrentStreak_IsZeroOnceAMonthHasActuallyBeenMissed()
    {
        var now = DateTime.UtcNow;
        var months = Months(
            $"{now.AddMonths(-5):yyyy-MM}",
            $"{now.AddMonths(-4):yyyy-MM}",
            $"{now.AddMonths(-3):yyyy-MM}");

        Assert.Equal(0, CurrentStreak(months));
    }

    [Fact]
    public void CurrentStreak_OfNothingIsZero()
    {
        Assert.Equal(0, CurrentStreak(new List<DateTime>()));
    }

    // ── Catalogue integrity ───────────────────────────────────────────────────

    [Fact]
    public void EveryAchievementKeyIsUnique()
    {
        // Keys are permanent: a scholar who earned one keeps it forever, so reusing a key
        // for a different badge would silently relabel their history.
        var keys = AchievementCatalog.All.Select(a => a.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryThresholdKeyExistsInTheCatalogue()
    {
        // A threshold pointing at a missing key would award a badge that renders as blank.
        var referenced = AchievementCatalog.JournalStreaks
            .Concat(AchievementCatalog.JournalTotals)
            .Concat(AchievementCatalog.VolunteerHours)
            .Select(t => t.Key);

        foreach (var key in referenced)
            Assert.NotNull(AchievementCatalog.Find(key));
    }

    [Fact]
    public void ThresholdsAreOrderedHighestFirst()
    {
        // The evaluator relies on this ordering to award every tier a scholar has passed.
        AssertDescending(AchievementCatalog.JournalStreaks.Select(t => t.Threshold));
        AssertDescending(AchievementCatalog.JournalTotals.Select(t => t.Threshold));
        AssertDescending(AchievementCatalog.VolunteerHours.Select(t => t.Threshold));

        static void AssertDescending(IEnumerable<int> values)
        {
            var list = values.ToList();
            Assert.Equal(list.OrderByDescending(v => v).ToList(), list);
        }
    }

    [Fact]
    public void EveryAchievementHasNameDescriptionAndIcon()
    {
        foreach (var achievement in AchievementCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(achievement.Name), achievement.Key);
            Assert.False(string.IsNullOrWhiteSpace(achievement.Description), achievement.Key);
            Assert.False(string.IsNullOrWhiteSpace(achievement.Icon), achievement.Key);
        }
    }

    [Fact]
    public void ThereIsAnEarnableBadgeForABeginner()
    {
        // A badge nobody earns is decoration. Something must be reachable from a single
        // journal entry, or a new scholar sees only locked icons.
        Assert.NotNull(AchievementCatalog.Find("journal-first"));
        Assert.NotNull(AchievementCatalog.Find("kudos-first-given"));
    }

    // ── Kudos categories ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("helpful", true)]
    [InlineData("leadership", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("rude", false)]
    [InlineData("HELPFUL", false)]
    public void KudosCategoryValidation(string? category, bool expected)
    {
        Assert.Equal(expected, KudosCategories.IsValid(category));
    }

    [Fact]
    public void EveryKudosCategoryIsPositive()
    {
        // Pinning the intent: recognition is positive-only. A negative category would turn
        // this into a rating system between people who know each other.
        var negativeWords = new[] { "bad", "poor", "worst", "dislike", "complaint", "weak" };

        foreach (var label in KudosCategories.All.Values)
        {
            foreach (var word in negativeWords)
                Assert.DoesNotContain(word, label, StringComparison.OrdinalIgnoreCase);
        }
    }
}
