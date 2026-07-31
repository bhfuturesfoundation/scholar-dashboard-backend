using Auth.Models.Enums.Notifications;

namespace Auth.Models.DTOs.Notifications
{
    /// <summary>
    /// A notification as the client sees it.
    ///
    /// Carries <see cref="MessageKey"/> and <see cref="Params"/> rather than a rendered
    /// sentence — the browser knows which language the reader has chosen, the server does
    /// not. <see cref="FallbackText"/> exists only so an unrecognised key (a notification
    /// sent by a newer backend than the deployed frontend) still shows something readable
    /// instead of a raw key.
    /// </summary>
    public class NotificationDto
    {
        public int Id { get; set; }
        public string MessageKey { get; set; } = string.Empty;
        public Dictionary<string, string> Params { get; set; } = new();
        public NotificationCategory Category { get; set; }
        public string? ActionUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool Read { get; set; }
        public int CollapseCount { get; set; } = 1;

        /// <summary>English rendering, used only when the client has no string for the key.</summary>
        public string FallbackText { get; set; } = string.Empty;
    }

    public class NotificationListDto
    {
        public List<NotificationDto> Items { get; set; } = new();
        public int UnreadCount { get; set; }

        /// <summary>Total undismissed, so the client knows whether more exist beyond the page.</summary>
        public int TotalCount { get; set; }
    }

    public class NotificationPreferenceDto
    {
        public bool EmailJournal { get; set; }
        public bool EmailKudos { get; set; }
        public bool EmailAchievements { get; set; }
        public bool EmailAnnouncements { get; set; }
        public bool EmailMentorship { get; set; }
        public bool EmailWeeklyDigest { get; set; }

        public bool PushJournal { get; set; }
        public bool PushKudos { get; set; }
        public bool PushAchievements { get; set; }
        public bool PushAnnouncements { get; set; }
        public bool PushMentorship { get; set; }
        public bool PushMinigames { get; set; }

        public bool QuietHoursEnabled { get; set; }
        public int QuietHoursStart { get; set; }
        public int QuietHoursEnd { get; set; }
        public string TimeZoneId { get; set; } = "Europe/Sarajevo";

        /// <summary>Language for email and push. The in-app bell uses the browser's choice.</summary>
        public string PreferredLocale { get; set; } = "bs";

        /// <summary>
        /// Whether push can work at all for this deployment. The preferences screen hides
        /// the push column when VAPID is unconfigured, rather than offering switches that
        /// silently do nothing.
        /// </summary>
        public bool PushAvailable { get; set; }

        /// <summary>The VAPID public key the browser needs to subscribe. Null when unconfigured.</summary>
        public string? PushPublicKey { get; set; }

        /// <summary>How many devices this person currently has subscribed.</summary>
        public int PushDeviceCount { get; set; }
    }

    public class PushSubscriptionRequest
    {
        public string Endpoint { get; set; } = string.Empty;
        public string P256dh { get; set; } = string.Empty;
        public string Auth { get; set; } = string.Empty;
        public string? UserAgent { get; set; }
    }

    // ── Journal window ────────────────────────────────────────────────────────

    /// <summary>
    /// The submission window, computed server-side.
    ///
    /// Previously the frontend built this itself from <c>new Date(y, m, 9, 23, 59, 59)</c> —
    /// a business rule the backend also needs for reminders, duplicated, and evaluated in
    /// the browser's local time so a scholar travelling saw a different deadline than the
    /// one the server would enforce. Every field here is a UTC instant.
    /// </summary>
    public class JournalWindowDto
    {
        /// <summary>The month being reported on, e.g. <c>2026-06</c>.</summary>
        public string MonthYear { get; set; } = string.Empty;

        /// <summary>English month label for display fallback, e.g. <c>June 2026</c>.</summary>
        public string MonthLabel { get; set; } = string.Empty;

        public DateTime OpensAtUtc { get; set; }
        public DateTime ClosesAtUtc { get; set; }

        /// <summary>True when the window is currently accepting submissions.</summary>
        public bool IsOpen { get; set; }

        /// <summary>Whole days remaining, floor 0. Null once closed.</summary>
        public int? DaysRemaining { get; set; }

        /// <summary>Whether this scholar has already submitted for the month.</summary>
        public bool Submitted { get; set; }

        /// <summary>
        /// Whether the API rejects a submission after <see cref="ClosesAtUtc"/>. Off by
        /// default — see <c>JournalWindowService</c>.
        /// </summary>
        public bool Enforced { get; set; }
    }

    // ── Announcements ─────────────────────────────────────────────────────────

    public class AnnouncementRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? ActionUrl { get; set; }
        public string? ActionLabel { get; set; }

        public List<string>? TargetRoles { get; set; }
        public int? TargetGenerationId { get; set; }
        public int? TargetStatus { get; set; }
        public bool IncludeInactive { get; set; }

        public bool SendEmail { get; set; }
        public bool SendPush { get; set; }
    }

    public class AnnouncementDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? ActionUrl { get; set; }
        public string? ActionLabel { get; set; }
        public string? TargetRoles { get; set; }
        public int? TargetGenerationId { get; set; }
        public int? TargetStatus { get; set; }
        public bool IncludeInactive { get; set; }
        public bool SendEmail { get; set; }
        public bool SendPush { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public int RecipientCount { get; set; }
    }

    /// <summary>
    /// Who an announcement would reach, shown before it is sent.
    ///
    /// Preview-then-send, the same shape used for bulk promotion and firm import: a
    /// broadcast is not revocable once it has hit a few hundred inboxes, so the count and a
    /// sample of names are worth one extra click.
    /// </summary>
    public class AudiencePreviewDto
    {
        public int TotalRecipients { get; set; }

        /// <summary>How many of those would also receive an email, after preferences.</summary>
        public int EmailRecipients { get; set; }

        /// <summary>How many devices would receive a push, after preferences.</summary>
        public int PushDevices { get; set; }

        /// <summary>A handful of names so the sender can sanity-check the filter.</summary>
        public List<string> SampleNames { get; set; } = new();

        /// <summary>Anything the sender should know — an empty audience, mostly.</summary>
        public List<string> Warnings { get; set; } = new();
    }
}
