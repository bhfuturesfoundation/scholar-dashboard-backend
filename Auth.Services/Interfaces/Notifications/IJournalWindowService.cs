using Auth.Models.DTOs.Notifications;

namespace Auth.Services.Interfaces.Notifications
{
    /// <summary>
    /// Owns the journal submission window: when it opens, when it closes, and which month
    /// it collects.
    ///
    /// This rule previously existed only in the browser, as
    /// <c>new Date(year, month, 9, 23, 59, 59)</c> inside a React effect. That had three
    /// consequences worth naming, because they are why this class exists:
    ///
    /// 1. The server needed the same rule to send reminders and had no copy of it.
    /// 2. The deadline was computed in the browser's local time, so the same moment was
    ///    "two days left" in Sarajevo and "three days left" for a scholar studying abroad.
    /// 3. Nothing enforced it. The submit endpoint accepted any <c>monthYear</c> at any
    ///    time, so the window was advisory decoration on top of an open API.
    /// </summary>
    public interface IJournalWindowService
    {
        /// <summary>
        /// The window that governs <paramref name="utcNow"/> — the one currently open, or
        /// the one that most recently closed if today falls outside it.
        /// </summary>
        JournalWindow GetCurrentWindow(DateTime utcNow);

        /// <summary>The window during which <paramref name="monthYear"/> may be submitted.</summary>
        JournalWindow GetWindowForMonth(string monthYear);

        /// <summary>
        /// The window as one scholar sees it, including whether they have already submitted.
        /// </summary>
        Task<JournalWindowDto> GetForScholarAsync(
            string scholarId, DateTime utcNow, CancellationToken cancellationToken = default);

        /// <summary>
        /// Whether the API rejects submissions outside the window.
        ///
        /// Off unless <c>JOURNAL_ENFORCE_WINDOW=true</c>. Turning it on is a real behaviour
        /// change — staff have historically been able to submit on a scholar's behalf after
        /// the fact, and that would start failing — so it is opt-in rather than a silent
        /// tightening on deploy.
        /// </summary>
        bool IsEnforced { get; }
    }

    /// <summary>
    /// A submission window. Every instant is UTC; the local-time rule that produced it has
    /// already been applied.
    /// </summary>
    public record JournalWindow(
        string MonthYear,
        string MonthLabel,
        DateTime OpensAtUtc,
        DateTime ClosesAtUtc)
    {
        public bool IsOpenAt(DateTime utcNow) => utcNow >= OpensAtUtc && utcNow <= ClosesAtUtc;

        /// <summary>
        /// Whole days from <paramref name="utcNow"/> until close, floored at 0. Null once the
        /// window has closed, so callers cannot accidentally render a negative countdown.
        /// </summary>
        public int? DaysRemainingAt(DateTime utcNow)
        {
            if (utcNow > ClosesAtUtc) return null;
            var remaining = ClosesAtUtc - utcNow;
            return remaining.TotalDays < 0 ? 0 : (int)Math.Floor(remaining.TotalDays);
        }
    }
}
