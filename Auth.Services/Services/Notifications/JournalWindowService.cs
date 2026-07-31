using System.Globalization;
using Auth.Models.Data;
using Auth.Models.DTOs.Notifications;
using Auth.Services.Interfaces.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Notifications
{
    /// <summary>
    /// The submission window, computed once, on the server, in UTC.
    ///
    /// The programme's rule is "you report on last month, during the first nine days of
    /// this one". The nine and the time zone are configurable because a programme rule
    /// should not need a deploy to change, but the defaults reproduce exactly what the
    /// frontend used to do so nothing shifts under anyone on the day this ships.
    /// </summary>
    public class JournalWindowService : IJournalWindowService
    {
        private const int DefaultCloseDay = 9;
        private const string DefaultTimeZone = "Europe/Sarajevo";

        private static readonly string[] MonthLabels =
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<JournalWindowService> _logger;

        public JournalWindowService(
            ApplicationDbContext context,
            IConfiguration configuration,
            ILogger<JournalWindowService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public bool IsEnforced =>
            string.Equals(_configuration["JOURNAL_ENFORCE_WINDOW"]?.Trim(), "true",
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Last day of the window, inclusive. Clamped to 1–28 so every month has the day —
        /// a configured 30 would silently skip February.
        /// </summary>
        private int CloseDay
        {
            get
            {
                var configured = _configuration["JOURNAL_WINDOW_CLOSE_DAY"];
                if (int.TryParse(configured, out var day) && day is >= 1 and <= 28) return day;
                return DefaultCloseDay;
            }
        }

        /// <summary>
        /// The programme's own time zone. The window is a human rule — "by the 9th" means
        /// the end of the 9th where the foundation is, not wherever a scholar happens to be
        /// studying — so it is anchored here and converted to UTC once.
        /// </summary>
        private TimeZoneInfo ProgrammeZone
        {
            get
            {
                var id = _configuration["JOURNAL_TIMEZONE"];
                if (string.IsNullOrWhiteSpace(id)) id = DefaultTimeZone;

                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    // UTC rather than a throw: a bad configuration value must not take the
                    // journal down. Sarajevo is UTC+1/+2, so the deadline moves by an hour or
                    // two — visible in the logs, survivable in production.
                    _logger.LogError(
                        "JOURNAL_TIMEZONE '{Zone}' is not a known time zone. Falling back to UTC, " +
                        "which shifts the submission deadline by up to two hours.", id);
                    return TimeZoneInfo.Utc;
                }
            }
        }

        public JournalWindow GetCurrentWindow(DateTime utcNow)
        {
            // "Which month are we collecting?" is decided in programme-local time, because
            // for the two hours after midnight UTC on the 1st it is already the 1st in
            // Sarajevo — and the scholar looking at the app is being told the window is open.
            var local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), ProgrammeZone);

            var reportingMonth = new DateTime(local.Year, local.Month, 1).AddMonths(-1);
            return BuildWindow(reportingMonth);
        }

        public JournalWindow GetWindowForMonth(string monthYear)
        {
            if (!TryParseMonth(monthYear, out var month))
            {
                throw new ArgumentException($"'{monthYear}' is not a valid yyyy-MM month.", nameof(monthYear));
            }

            return BuildWindow(month);
        }

        /// <summary>
        /// Builds the window for a reporting month. The window sits in the month *after* the
        /// one being reported on: June's journal is submitted between 1 and 9 July.
        /// </summary>
        private JournalWindow BuildWindow(DateTime reportingMonth)
        {
            var zone = ProgrammeZone;
            var submissionMonth = reportingMonth.AddMonths(1);

            var opensLocal = new DateTime(submissionMonth.Year, submissionMonth.Month, 1, 0, 0, 0);

            // End of the closing day, not the start of it: "submit by the 9th" means the 9th
            // counts. Using the first tick of the 10th minus one avoids the classic
            // 23:59:59 gap that silently drops the final second.
            var closesLocal = new DateTime(submissionMonth.Year, submissionMonth.Month, CloseDay, 0, 0, 0)
                .AddDays(1)
                .AddTicks(-1);

            return new JournalWindow(
                MonthYear: $"{reportingMonth.Year:0000}-{reportingMonth.Month:00}",
                MonthLabel: $"{MonthLabels[reportingMonth.Month - 1]} {reportingMonth.Year}",
                OpensAtUtc: ToUtc(opensLocal, zone),
                ClosesAtUtc: ToUtc(closesLocal, zone));
        }

        /// <summary>
        /// Converts a programme-local wall-clock time to UTC, tolerating the two awkward
        /// cases daylight saving produces. Neither can actually occur with a 1st-of-month
        /// midnight or an end-of-day boundary under current European rules, but a changed
        /// <c>JOURNAL_WINDOW_CLOSE_DAY</c> plus a future rule change could reach them, and
        /// <c>ConvertTimeToUtc</c> throws rather than picking for you.
        /// </summary>
        private static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
        {
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

            // Spring forward: this wall-clock time never happened. Step past the gap.
            if (zone.IsInvalidTime(unspecified))
            {
                unspecified = unspecified.AddHours(1);
            }

            // Autumn back: this wall-clock time happened twice. ConvertTimeToUtc picks the
            // standard-time (later) instant, which for a deadline is the more generous read.
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        }

        public async Task<JournalWindowDto> GetForScholarAsync(
            string scholarId, DateTime utcNow, CancellationToken cancellationToken = default)
        {
            var window = GetCurrentWindow(utcNow);

            var submitted = await _context.JournalSubmissions
                .AsNoTracking()
                .AnyAsync(s => s.ScholarId == scholarId
                            && s.MonthYear == window.MonthYear
                            && s.Submitted,
                    cancellationToken);

            return new JournalWindowDto
            {
                MonthYear = window.MonthYear,
                MonthLabel = window.MonthLabel,
                OpensAtUtc = window.OpensAtUtc,
                ClosesAtUtc = window.ClosesAtUtc,
                IsOpen = window.IsOpenAt(utcNow),
                DaysRemaining = window.DaysRemainingAt(utcNow),
                Submitted = submitted,
                Enforced = IsEnforced
            };
        }

        /// <summary>Parses <c>yyyy-MM</c> to the first day of that month.</summary>
        public static bool TryParseMonth(string? monthYear, out DateTime month)
        {
            month = default;
            if (string.IsNullOrWhiteSpace(monthYear)) return false;

            if (!DateTime.TryParseExact(
                    monthYear.Trim(), "yyyy-MM",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return false;
            }

            month = new DateTime(parsed.Year, parsed.Month, 1);
            return true;
        }
    }
}
