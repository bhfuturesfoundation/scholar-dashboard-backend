using Auth.Models.Enums.Notifications;

namespace Auth.Models.Entities.Notifications
{
    /// <summary>
    /// One thing a person was told, stored server-side.
    ///
    /// This used to live entirely in <c>localStorage</c> in the browser, which meant read
    /// state did not follow anyone between their laptop and their phone, clearing site data
    /// erased the history, and nothing could be asked of it after the fact — "was this
    /// scholar ever told the window was closing?" had no answer. Now that the app is
    /// installable as a PWA, per-device notification state is actively wrong rather than
    /// merely limited.
    ///
    /// The body is stored as <see cref="MessageKey"/> plus <see cref="ParamsJson"/> rather
    /// than a finished sentence, so the same row renders in whichever language the reader
    /// has selected.
    /// </summary>
    public class Notification
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        /// <summary>A key from <see cref="Constants.NotificationKeys"/>.</summary>
        public string MessageKey { get; set; } = string.Empty;

        /// <summary>
        /// JSON object of substitution values, e.g. <c>{"fromName":"Amina","monthLabel":"June"}</c>.
        /// Stored as JSON rather than as columns because the parameter set differs per key
        /// and is only ever read as a whole.
        /// </summary>
        public string? ParamsJson { get; set; }

        public NotificationCategory Category { get; set; }

        /// <summary>Relative path the reader is taken to on tap, e.g. <c>/journal</c>.</summary>
        public string? ActionUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Null until read. A timestamp rather than a bool so "when" is answerable.</summary>
        public DateTime? ReadAt { get; set; }

        /// <summary>
        /// Null until the reader dismisses it. Dismissal hides rather than deletes, so the
        /// record of what someone was told survives them clearing their bell menu.
        /// </summary>
        public DateTime? DismissedAt { get; set; }

        /// <summary>
        /// Idempotency key, unique per user when set. Replaces the old client-side
        /// <c>pushOnce</c> localStorage flags, which lived on one device and leaked a key
        /// per month per threshold that was never cleaned up.
        ///
        /// Example: <c>journal-due:2026-07:t-2</c>.
        /// </summary>
        public string? DedupeKey { get; set; }

        /// <summary>
        /// Groups notifications that should read as one line when several arrive close
        /// together, e.g. <c>kudos</c>. A well-liked scholar could otherwise collect a dozen
        /// separate entries in an afternoon, which reads as spam rather than recognition.
        /// </summary>
        public string? CollapseKey { get; set; }

        /// <summary>How many events this row represents. 1 unless it has collapsed.</summary>
        public int CollapseCount { get; set; } = 1;

        // ── Outbound delivery ─────────────────────────────────────────────────

        /// <summary>Set once an email has gone out for this notification.</summary>
        public DateTime? EmailSentAt { get; set; }

        /// <summary>Set once a push has been delivered to at least one subscription.</summary>
        public DateTime? PushSentAt { get; set; }

        /// <summary>
        /// When set, email and push are held until this instant — the notification is still
        /// visible in-app immediately. Used for quiet hours: something that happens at
        /// 01:00 should not wake a phone, but it should still be waiting in the morning.
        /// </summary>
        public DateTime? DeferredUntil { get; set; }

        /// <summary>
        /// True when the notification wants to leave the app. Set by the caller so a purely
        /// informational entry (an invite outcome, say) never triggers an email.
        /// </summary>
        public bool WantsEmail { get; set; }

        public bool WantsPush { get; set; }

        /// <summary>The announcement that produced this row, when it was a broadcast.</summary>
        public int? AnnouncementId { get; set; }
        public Announcement? Announcement { get; set; }
    }
}
