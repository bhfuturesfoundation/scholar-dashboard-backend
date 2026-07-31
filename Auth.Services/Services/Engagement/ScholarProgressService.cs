using Auth.Models.Data;
using Auth.Models.DTOs.Engagement;
using Auth.Models.Entities.Engagement;
using Auth.Models.Enums.Scholars;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces.Engagement;
using Auth.Services.Interfaces.Notifications;
using Auth.Models.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Auth.Services.Services.Engagement
{
    /// <summary>
    /// What a scholar gets back for filling in their journal: their own trend, their streak,
    /// how they compare to their cohort, and the badges they've earned.
    ///
    /// The comparison is deliberately anonymous and aggregate-only — a cohort median, never
    /// another scholar's number. These are personal reflections including a satisfaction
    /// rating; turning that into a visible ranking between people who know each other would
    /// change what they write, which destroys the data and the point of the exercise.
    /// </summary>
    public class ScholarProgressService : IScholarProgressService
    {
        /// <summary>
        /// Minimum cohort size before a comparison is shown at all. Below this, "the cohort
        /// median" is one or two identifiable people.
        /// </summary>
        private const int MinimumCohortForComparison = 5;

        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notifications;
        private readonly ILogger<ScholarProgressService> _logger;

        public ScholarProgressService(
            ApplicationDbContext context,
            INotificationService notifications,
            ILogger<ScholarProgressService> logger)
        {
            _context = context;
            _notifications = notifications;
            _logger = logger;
        }

        public async Task<ScholarProgressDto> GetProgressAsync(
            string scholarId, CancellationToken cancellationToken = default)
        {
            var scholar = await _context.Users
                .AsNoTracking()
                .Include(u => u.Generation)
                .FirstOrDefaultAsync(u => u.Id == scholarId, cancellationToken)
                ?? throw new NotFoundException("Scholar", scholarId);

            var submissions = await _context.JournalSubmissions
                .AsNoTracking()
                .Where(js => js.ScholarId == scholarId && js.Submitted)
                .Select(js => js.MonthYear)
                .ToListAsync(cancellationToken);

            var months = submissions
                .Select(ParseMonth)
                .Where(m => m.HasValue)
                .Select(m => m!.Value)
                .OrderBy(m => m)
                .ToList();

            // Satisfaction comes from the skill questions (1-10 ratings), averaged per month.
            var skillQuestionIds = await _context.Questions
                .AsNoTracking()
                .Where(q => q.IsSkill)
                .Select(q => q.QuestionId)
                .ToListAsync(cancellationToken);

            var ratings = await _context.Answers
                .AsNoTracking()
                .Where(a => a.ScholarId == scholarId && skillQuestionIds.Contains(a.QuestionId))
                .Select(a => new { a.MonthYear, a.Response })
                .ToListAsync(cancellationToken);

            var trend = ratings
                .GroupBy(r => r.MonthYear)
                .Select(g => new ScholarTrendPointDto
                {
                    MonthYear = g.Key,
                    Label = FormatMonth(g.Key),
                    Score = AverageRating(g.Select(x => x.Response)),
                    Submitted = submissions.Contains(g.Key)
                })
                // Both conditions matter, and the second was missing.
                //
                // The trend is built from Answers, and an Answer row exists as soon as a
                // draft is auto-saved — long before anything is submitted. So a scholar who
                // typed a rating one month and never submitted still produced a point on
                // their chart, and it counted toward "latest score" and the trend arrow.
                //
                // JournalSubmissions is the authority on what was actually submitted; the
                // Submitted flag was already being computed here and then ignored.
                .Where(p => p.Score.HasValue && p.Submitted)
                .OrderBy(p => p.MonthYear, StringComparer.Ordinal)
                .ToList();

            var progress = new ScholarProgressDto
            {
                TotalSubmissions = months.Count,
                CurrentStreak = CurrentStreak(months),
                LongestStreak = LongestStreak(months),
                Trend = trend,
                GenerationName = scholar.Generation?.Name,
                Status = scholar.ScholarStatus.ToString(),
                LatestScore = trend.LastOrDefault()?.Score,
            };

            // Direction of travel over the last three points — more useful to a scholar than
            // the absolute number, which they can already see.
            if (trend.Count >= 2)
            {
                var recent = trend.TakeLast(3).ToList();
                var change = recent.Last().Score!.Value - recent.First().Score!.Value;
                progress.TrendDirection = change switch
                {
                    > 0.3 => "up",
                    < -0.3 => "down",
                    _ => "steady"
                };
                progress.TrendChange = Math.Round(change, 1);
            }

            await AddCohortComparisonAsync(progress, scholar.GenerationId, skillQuestionIds, cancellationToken);

            progress.Achievements = await GetAchievementsAsync(scholarId, cancellationToken);

            return progress;
        }

        /// <summary>
        /// Cohort median for the most recent month, shown only when the cohort is large
        /// enough that a median doesn't identify anyone.
        /// </summary>
        private async Task AddCohortComparisonAsync(
            ScholarProgressDto progress,
            int? generationId,
            List<int> skillQuestionIds,
            CancellationToken cancellationToken)
        {
            if (generationId is null || progress.Trend.Count == 0) return;

            var latestMonth = progress.Trend.Last().MonthYear;

            var cohortIds = await _context.Users
                .AsNoTracking()
                .Where(u => u.GenerationId == generationId)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            if (cohortIds.Count < MinimumCohortForComparison) return;

            // Only scholars who actually submitted that month.
            //
            // Same trap as the personal trend: an Answer row exists from the first
            // auto-saved draft, so without this the median was computed partly from ratings
            // nobody ever chose to submit — which is both wrong and a small privacy problem,
            // since it exposes the aggregate of text people decided not to send.
            var submittedCohortIds = await _context.JournalSubmissions
                .AsNoTracking()
                .Where(js => js.MonthYear == latestMonth
                          && js.Submitted
                          && cohortIds.Contains(js.ScholarId))
                .Select(js => js.ScholarId)
                .ToListAsync(cancellationToken);

            if (submittedCohortIds.Count < MinimumCohortForComparison) return;

            var cohortRatings = await _context.Answers
                .AsNoTracking()
                .Where(a => submittedCohortIds.Contains(a.ScholarId)
                            && a.MonthYear == latestMonth
                            && skillQuestionIds.Contains(a.QuestionId))
                .Select(a => new { a.ScholarId, a.Response })
                .ToListAsync(cancellationToken);

            var perScholar = cohortRatings
                .GroupBy(r => r.ScholarId)
                .Select(g => AverageRating(g.Select(x => x.Response)))
                .Where(score => score.HasValue)
                .Select(score => score!.Value)
                .OrderBy(score => score)
                .ToList();

            if (perScholar.Count < MinimumCohortForComparison) return;

            progress.CohortMedian = Math.Round(Median(perScholar), 1);
            progress.CohortSize = perScholar.Count;
        }

        public async Task<List<AchievementDto>> GetAchievementsAsync(
            string scholarId, CancellationToken cancellationToken = default)
        {
            var earned = await _context.Achievements
                .AsNoTracking()
                .Where(a => a.UserId == scholarId)
                .ToDictionaryAsync(a => a.Key, cancellationToken);

            // Every badge is returned, earned or not, so the UI can show what's still
            // reachable. A wall of locked badges is a roadmap; only showing earned ones tells
            // a new scholar nothing about what to aim for.
            return AchievementCatalog.All
                .Select(definition =>
                {
                    earned.TryGetValue(definition.Key, out var record);

                    return new AchievementDto
                    {
                        Key = definition.Key,
                        Name = definition.Name,
                        Description = definition.Description,
                        Category = definition.Category,
                        Tier = definition.Tier.ToString(),
                        Icon = definition.Icon,
                        IsEarned = record is not null,
                        EarnedAt = record?.EarnedAt,
                        IsNew = record is { IsSeen: false }
                    };
                })
                .ToList();
        }

        public async Task<int> EvaluateAsync(string scholarId, CancellationToken cancellationToken = default)
        {
            var existing = await _context.Achievements
                .Where(a => a.UserId == scholarId)
                .Select(a => a.Key)
                .ToListAsync(cancellationToken);

            var have = existing.ToHashSet(StringComparer.Ordinal);
            var toAward = new List<string>();

            void Award(string key)
            {
                if (!have.Contains(key)) toAward.Add(key);
            }

            // ── Journal ───────────────────────────────────────────────────────
            var months = (await _context.JournalSubmissions
                    .AsNoTracking()
                    .Where(js => js.ScholarId == scholarId && js.Submitted)
                    .Select(js => js.MonthYear)
                    .ToListAsync(cancellationToken))
                .Select(ParseMonth)
                .Where(m => m.HasValue)
                .Select(m => m!.Value)
                .OrderBy(m => m)
                .ToList();

            if (months.Count > 0) Award("journal-first");

            var longest = LongestStreak(months);
            foreach (var (threshold, key) in AchievementCatalog.JournalStreaks)
                if (longest >= threshold) Award(key);

            foreach (var (threshold, key) in AchievementCatalog.JournalTotals)
                if (months.Count >= threshold) Award(key);

            // ── Community ─────────────────────────────────────────────────────
            var given = await _context.Kudos
                .Where(k => k.FromUserId == scholarId && !k.IsHidden)
                .Select(k => k.ToUserId)
                .Distinct()
                .CountAsync(cancellationToken);

            if (given >= 1) Award("kudos-first-given");
            if (given >= 5) Award("kudos-5-given");

            var received = await _context.Kudos
                .CountAsync(k => k.ToUserId == scholarId && !k.IsHidden, cancellationToken);

            if (received >= 1) Award("kudos-first-received");
            if (received >= 10) Award("kudos-10-received");

            if (toAward.Count == 0) return 0;

            _context.Achievements.AddRange(toAward.Select(key => new Achievement
            {
                UserId = scholarId,
                Key = key,
                EarnedAt = DateTime.UtcNow,
                IsSeen = false
            }));

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Awarded {Count} achievement(s) to {Scholar}: {Keys}",
                toAward.Count, scholarId, string.Join(", ", toAward));

            // One notification per badge, collapsed under a shared key. EvaluateAsync runs on
            // every progress-page load, so the dedupe key per badge is what stops a scholar
            // being told about the same badge on every visit.
            foreach (var key in toAward)
            {
                await _notifications.CreateAsync(new CreateNotificationRequest
                {
                    UserId = scholarId,
                    MessageKey = NotificationKeys.AchievementEarned,
                    Params = new Dictionary<string, string>
                    {
                        ["badgeName"] = AchievementCatalog.Find(key)?.Name ?? key
                    },
                    DedupeKey = $"achievement:{key}",
                    CollapseKey = "achievement",
                    WantsEmail = true,
                    WantsPush = true
                }, cancellationToken);
            }

            return toAward.Count;
        }

        public async Task MarkAchievementsSeenAsync(string scholarId, CancellationToken cancellationToken = default)
        {
            var unseen = await _context.Achievements
                .Where(a => a.UserId == scholarId && !a.IsSeen)
                .ToListAsync(cancellationToken);

            if (unseen.Count == 0) return;

            foreach (var achievement in unseen) achievement.IsSeen = true;
            await _context.SaveChangesAsync(cancellationToken);
        }

        // ── Streak arithmetic ─────────────────────────────────────────────────

        /// <summary>
        /// Consecutive months up to and including the most recent submission.
        ///
        /// Counted from the latest submission backwards rather than from today, so a scholar
        /// who has not yet filled in the current month doesn't see their streak drop to zero
        /// on the 1st — it breaks only when a month is actually skipped.
        /// </summary>
        private static int CurrentStreak(List<DateTime> months)
        {
            if (months.Count == 0) return 0;

            var streak = 1;

            for (var i = months.Count - 1; i > 0; i--)
            {
                var gap = ((months[i].Year - months[i - 1].Year) * 12) + months[i].Month - months[i - 1].Month;
                if (gap != 1) break;
                streak++;
            }

            // A streak that ended more than a month ago is history, not a current streak.
            var now = DateTime.UtcNow;
            var monthsSinceLast = ((now.Year - months[^1].Year) * 12) + now.Month - months[^1].Month;

            return monthsSinceLast > 1 ? 0 : streak;
        }

        private static int LongestStreak(List<DateTime> months)
        {
            if (months.Count == 0) return 0;

            var longest = 1;
            var current = 1;

            for (var i = 1; i < months.Count; i++)
            {
                var gap = ((months[i].Year - months[i - 1].Year) * 12) + months[i].Month - months[i - 1].Month;

                if (gap == 1) current++;
                else if (gap > 1) current = 1;
                // gap == 0 is a duplicate month; neither extends nor breaks the run.

                longest = Math.Max(longest, current);
            }

            return longest;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Parses "2026-07" to a date. Returns null for anything unparseable.</summary>
        private static DateTime? ParseMonth(string monthYear) =>
            DateTime.TryParseExact($"{monthYear}-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : null;

        private static string FormatMonth(string monthYear)
        {
            var parsed = ParseMonth(monthYear);
            return parsed?.ToString("MMM yyyy", CultureInfo.InvariantCulture) ?? monthYear;
        }

        /// <summary>
        /// Mean of the numeric responses in a group. Non-numeric answers are ignored rather
        /// than counted as zero, which would drag an average down for no reason.
        /// </summary>
        private static double? AverageRating(IEnumerable<string> responses)
        {
            var values = responses
                .Select(r => double.TryParse(r, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (double?)null)
                .Where(v => v.HasValue && v.Value is >= 1 and <= 10)
                .Select(v => v!.Value)
                .ToList();

            return values.Count == 0 ? null : Math.Round(values.Average(), 1);
        }

        private static double Median(List<double> sorted)
        {
            var mid = sorted.Count / 2;
            return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2 : sorted[mid];
        }
    }
}
