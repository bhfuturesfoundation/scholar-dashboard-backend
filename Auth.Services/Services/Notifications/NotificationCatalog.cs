using System.Text.RegularExpressions;
using Auth.Models.Constants;

namespace Auth.Services.Services.Notifications
{
    /// <summary>
    /// Server-side text for notifications, in both languages.
    ///
    /// The in-app bell renders from the frontend dictionary, because the browser knows which
    /// language the reader picked. Email and push do not have that luxury — nobody is
    /// looking at a React tree when a reminder goes out at 08:00 — so the server needs its
    /// own copy of the same strings.
    ///
    /// That duplication is deliberate and worth its cost: the alternative is rendering
    /// finished sentences at creation time and storing those, which is exactly the mistake
    /// this whole redesign exists to undo. The two copies are kept honest by a test that
    /// asserts every key in <see cref="NotificationKeys"/> has an entry here.
    /// </summary>
    public static class NotificationCatalog
    {
        public const string DefaultLocale = "bs";

        private static readonly Regex Placeholder = new(@"\{(\w+)\}", RegexOptions.Compiled);

        private record Entry(string Subject, string Body);

        // ── English ───────────────────────────────────────────────────────────

        private static readonly Dictionary<string, Entry> English = new(StringComparer.Ordinal)
        {
            [NotificationKeys.JournalDue] = new(
                "Your {monthLabel} journal is due in {daysLeft} day(s)",
                "Your journal for {monthLabel} hasn't been submitted yet. The window closes on {deadline}."),

            [NotificationKeys.JournalDueToday] = new(
                "Last day to submit your {monthLabel} journal",
                "The submission window for {monthLabel} closes today at {deadline}. It only takes a few minutes."),

            [NotificationKeys.JournalWindowClosed] = new(
                "The {monthLabel} journal window has closed",
                "The submission window for {monthLabel} closed on {deadline}. If you were unable to submit, speak to your program manager."),

            [NotificationKeys.JournalReceived] = new(
                "Your {monthLabel} journal was received",
                "Thanks — your journal for {monthLabel} is in. You can see how your submissions are trending on your progress page."),

            [NotificationKeys.KudosReceived] = new(
                "{fromName} recognised you",
                "{fromName} sent you kudos for {categoryLabel}."),

            [NotificationKeys.KudosReceivedMany] = new(
                "You received kudos",
                "{count} people in your generation sent you kudos."),

            [NotificationKeys.AchievementEarned] = new(
                "Badge earned: {badgeName}",
                "You've earned the {badgeName} badge."),

            [NotificationKeys.AchievementEarnedMany] = new(
                "You earned new badges",
                "You've earned {count} new badges."),

            [NotificationKeys.JournalReviewed] = new(
                "Your {monthLabel} journal was reviewed",
                "{reviewerName} has read your journal for {monthLabel}."),

            [NotificationKeys.MenteeSubmitted] = new(
                "{menteeName} submitted their journal",
                "{menteeName} has submitted their journal for {monthLabel}."),

            [NotificationKeys.StatusChanged] = new(
                "Your status is now {statusLabel}",
                "Your standing in the programme has been updated to {statusLabel}."),

            [NotificationKeys.SuggestionStatusChanged] = new(
                "Your suggestion is now {status}",
                "Your suggestion \"{excerpt}\" has been moved to {status}."),

            [NotificationKeys.Announcement] = new(
                "{title}",
                "{body}"),

            [NotificationKeys.Welcome] = new(
                "Welcome to the Scholar Dashboard",
                "Your account is ready. Sign in to complete your first monthly journal."),
        };

        // ── Bosnian ───────────────────────────────────────────────────────────
        //
        // Gender-neutral throughout: the recipient's gender is unknown, and informal
        // address would force a masculine or feminine participle ending. Anything that
        // would have needed one is rephrased so the journal, not the reader, is the
        // subject ("Vaš dnevnik je zaprimljen").
        //
        // Counts are phrased to survive Bosnian's three numeral case forms, which this
        // system cannot express: "Broj osoba: {count}" rather than "{count} osoba".

        private static readonly Dictionary<string, Entry> Bosnian = new(StringComparer.Ordinal)
        {
            [NotificationKeys.JournalDue] = new(
                "Rok za dnevnik za {monthLabel} ističe za {daysLeft} dan(a)",
                "Vaš dnevnik za {monthLabel} još nije predan. Rok za predaju ističe {deadline}."),

            [NotificationKeys.JournalDueToday] = new(
                "Posljednji dan za predaju dnevnika za {monthLabel}",
                "Rok za predaju dnevnika za {monthLabel} ističe danas u {deadline}. Potrebno je samo nekoliko minuta."),

            [NotificationKeys.JournalWindowClosed] = new(
                "Rok za dnevnik za {monthLabel} je zatvoren",
                "Rok za predaju dnevnika za {monthLabel} zatvoren je {deadline}. Ako predaja nije bila moguća, obratite se svom programskom menadžeru."),

            [NotificationKeys.JournalReceived] = new(
                "Vaš dnevnik za {monthLabel} je zaprimljen",
                "Hvala — dnevnik za {monthLabel} je zaprimljen. Svoj napredak možete pratiti na stranici napretka."),

            [NotificationKeys.KudosReceived] = new(
                "Dobili ste pohvalu od osobe {fromName}",
                "{fromName} vam je uputio/la pohvalu za: {categoryLabel}."),

            [NotificationKeys.KudosReceivedMany] = new(
                "Dobili ste pohvale",
                "Broj osoba iz vaše generacije koje su vam uputile pohvalu: {count}."),

            [NotificationKeys.AchievementEarned] = new(
                "Osvojena značka: {badgeName}",
                "Osvojili ste značku {badgeName}."),

            [NotificationKeys.AchievementEarnedMany] = new(
                "Osvojene su nove značke",
                "Broj novih znački: {count}."),

            [NotificationKeys.JournalReviewed] = new(
                "Vaš dnevnik za {monthLabel} je pregledan",
                "{reviewerName} je pročitao/la vaš dnevnik za {monthLabel}."),

            [NotificationKeys.MenteeSubmitted] = new(
                "{menteeName} je predao/la dnevnik",
                "{menteeName} je predao/la dnevnik za {monthLabel}."),

            [NotificationKeys.StatusChanged] = new(
                "Vaš status je sada: {statusLabel}",
                "Vaš status u programu promijenjen je u: {statusLabel}."),

            [NotificationKeys.SuggestionStatusChanged] = new(
                "Vaš prijedlog je sada: {status}",
                "Vaš prijedlog \"{excerpt}\" prebačen je u status: {status}."),

            [NotificationKeys.Announcement] = new(
                "{title}",
                "{body}"),

            [NotificationKeys.Welcome] = new(
                "Dobro došli u Panel stipendiste",
                "Vaš račun je spreman. Prijavite se kako biste popunili svoj prvi mjesečni dnevnik."),
        };

        private static Dictionary<string, Entry> For(string? locale) =>
            string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase) ? English : Bosnian;

        /// <summary>Email subject line for a key, with parameters substituted.</summary>
        public static string Subject(string key, IReadOnlyDictionary<string, string>? parameters, string? locale = null)
            => Render(Lookup(key, locale)?.Subject, parameters, key);

        /// <summary>Plain-text body for a key, with parameters substituted.</summary>
        public static string Body(string key, IReadOnlyDictionary<string, string>? parameters, string? locale = null)
            => Render(Lookup(key, locale)?.Body, parameters, key);

        /// <summary>
        /// English one-liner, used as <c>NotificationDto.FallbackText</c> so a client that
        /// does not yet know a newly added key shows a readable sentence instead of the raw
        /// key. Deliberately English: the fallback exists for an out-of-date frontend, and
        /// English is what every such build already contains.
        /// </summary>
        public static string FallbackText(string key, IReadOnlyDictionary<string, string>? parameters)
            => Render(English.TryGetValue(key, out var entry) ? entry.Body : null, parameters, key);

        public static bool HasKey(string key) => English.ContainsKey(key) && Bosnian.ContainsKey(key);

        /// <summary>Every key with text on both sides — used by the coverage test.</summary>
        public static IEnumerable<string> KnownKeys => English.Keys;

        private static Entry? Lookup(string key, string? locale)
        {
            var table = For(locale);
            if (table.TryGetValue(key, out var entry)) return entry;

            // Fall through to English rather than returning nothing: a key added to the
            // English table but not yet translated should still send something.
            return English.TryGetValue(key, out var fallback) ? fallback : null;
        }

        private static string Render(string? template, IReadOnlyDictionary<string, string>? parameters, string key)
        {
            if (string.IsNullOrEmpty(template)) return key;
            if (parameters is null || parameters.Count == 0) return template;

            // An unmatched placeholder is left as-is rather than blanked, so a missing
            // parameter shows up as "{monthLabel}" in a test inbox instead of silently
            // producing a sentence with a hole in it.
            return Placeholder.Replace(template, match =>
                parameters.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
        }
    }
}
