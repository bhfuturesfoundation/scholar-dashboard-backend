using Auth.Models.Enums.Notifications;

namespace Auth.Models.Constants
{
    /// <summary>
    /// Every notification the platform can produce, as a stable key.
    ///
    /// Notifications are stored as a key plus parameters rather than a finished sentence.
    /// The old client-side system baked English strings at creation time — which meant a
    /// scholar reading the app in Bosnian still got "Achievement unlocked: ...", and it
    /// meant that once those strings were persisted the language they were written in
    /// became permanent. A key survives translation, rewording, and a change of tone.
    ///
    /// These strings are written to the database. Renaming one orphans every notification
    /// already sent under the old name, so treat them as permanent: add a new key and stop
    /// using the old one instead.
    /// </summary>
    public static class NotificationKeys
    {
        // ── Journal ───────────────────────────────────────────────────────────
        /// <summary>Params: monthLabel, daysLeft, deadline.</summary>
        public const string JournalDue = "journal.due";

        /// <summary>Params: monthLabel, deadline.</summary>
        public const string JournalDueToday = "journal.dueToday";

        /// <summary>Params: monthLabel, deadline.</summary>
        public const string JournalWindowClosed = "journal.windowClosed";

        /// <summary>Params: monthLabel.</summary>
        public const string JournalReceived = "journal.received";

        // ── Engagement ────────────────────────────────────────────────────────
        /// <summary>Params: fromName, categoryLabel. Collapses.</summary>
        public const string KudosReceived = "kudos.received";

        /// <summary>Params: count. The collapsed form of several kudos at once.</summary>
        public const string KudosReceivedMany = "kudos.receivedMany";

        /// <summary>Params: badgeName.</summary>
        public const string AchievementEarned = "achievement.earned";

        /// <summary>Params: count.</summary>
        public const string AchievementEarnedMany = "achievement.earnedMany";

        // ── Mentorship and standing ───────────────────────────────────────────
        /// <summary>Params: reviewerName, monthLabel.</summary>
        public const string JournalReviewed = "mentorship.journalReviewed";

        /// <summary>Params: menteeName, monthLabel. Sent to the mentor.</summary>
        public const string MenteeSubmitted = "mentorship.menteeSubmitted";

        /// <summary>Params: statusLabel.</summary>
        public const string StatusChanged = "mentorship.statusChanged";

        /// <summary>Params: status, excerpt. Sent to the author when staff triage it.</summary>
        public const string SuggestionStatusChanged = "suggestion.statusChanged";

        // ── Social ────────────────────────────────────────────────────────────
        /// <summary>Params: fromName. Someone said hello and nothing more.</summary>
        public const string Poked = "social.poked";

        /// <summary>Params: fromName, gameName. Carries a link straight into the match.</summary>
        public const string MinigameInvite = "minigame.invite";

        // ── Broadcast ─────────────────────────────────────────────────────────
        /// <summary>Params: title, body. Written by staff, not translated.</summary>
        public const string Announcement = "announcement.custom";

        // ── System ────────────────────────────────────────────────────────────
        public const string Welcome = "system.welcome";

        /// <summary>
        /// Which category each key belongs to, and therefore which preference switch
        /// governs it. A key missing from this map is treated as <see cref="NotificationCategory.System"/>,
        /// which is the safe default: System cannot be muted, so a new key can never be
        /// silently swallowed because someone forgot to register it.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, NotificationCategory> Categories =
            new Dictionary<string, NotificationCategory>(StringComparer.Ordinal)
            {
                [JournalDue] = NotificationCategory.Journal,
                [JournalDueToday] = NotificationCategory.Journal,
                [JournalWindowClosed] = NotificationCategory.Journal,
                [JournalReceived] = NotificationCategory.Journal,

                [KudosReceived] = NotificationCategory.Kudos,
                [KudosReceivedMany] = NotificationCategory.Kudos,

                [AchievementEarned] = NotificationCategory.Achievement,
                [AchievementEarnedMany] = NotificationCategory.Achievement,

                [JournalReviewed] = NotificationCategory.Mentorship,
                [MenteeSubmitted] = NotificationCategory.Mentorship,
                [StatusChanged] = NotificationCategory.Mentorship,

                [SuggestionStatusChanged] = NotificationCategory.Mentorship,

                [Poked] = NotificationCategory.Minigame,
                [MinigameInvite] = NotificationCategory.Minigame,

                [Announcement] = NotificationCategory.Announcement,

                [Welcome] = NotificationCategory.System,
            };

        /// <summary>
        /// Where tapping the notification should take the reader. A notification that
        /// reports something without offering the next step makes the reader go find it,
        /// which for a deadline reminder on a phone is most of the friction.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> DefaultActions =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [JournalDue] = "/journal",
                [JournalDueToday] = "/journal",
                [JournalWindowClosed] = "/journal",
                [JournalReceived] = "/progress",

                [KudosReceived] = "/progress",
                [KudosReceivedMany] = "/progress",
                [AchievementEarned] = "/progress",
                [AchievementEarnedMany] = "/progress",

                [JournalReviewed] = "/journal",
                [MenteeSubmitted] = "/mentor/journals",
                [StatusChanged] = "/progress",

                [SuggestionStatusChanged] = "/suggestions",

                [Welcome] = "/",
            };

        public static NotificationCategory CategoryFor(string key) =>
            Categories.TryGetValue(key, out var category) ? category : NotificationCategory.System;

        public static string? ActionFor(string key) =>
            DefaultActions.TryGetValue(key, out var action) ? action : null;
    }
}
