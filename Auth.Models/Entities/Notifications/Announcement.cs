namespace Auth.Models.Entities.Notifications
{
    /// <summary>
    /// A staff-authored broadcast, kept as its own record rather than only as the
    /// notifications it produced.
    ///
    /// The previous approach was a string compiled into the frontend — "The Gaming Update
    /// is live" was seeded into any account with an empty cache, which meant it kept
    /// arriving for scholars who joined long after that release, and reappeared for anyone
    /// who cleared their browser data. Sending an announcement should not require a deploy,
    /// and it should be possible afterwards to say who received it.
    /// </summary>
    public class Announcement
    {
        public int Id { get; set; }

        /// <summary>Written by staff and shown as-is. Not a translation key.</summary>
        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        /// <summary>Optional relative path, e.g. <c>/minigames</c>.</summary>
        public string? ActionUrl { get; set; }

        /// <summary>Label for the action button. Ignored when <see cref="ActionUrl"/> is null.</summary>
        public string? ActionLabel { get; set; }

        // ── Targeting ─────────────────────────────────────────────────────────
        //
        // Null means "no filter on this dimension". All supplied filters are ANDed, which
        // is the reading people expect from a form: ticking Senior and Mentor means
        // seniors who are mentors, not seniors plus every mentor.

        /// <summary>Comma-separated role names, or null for every role.</summary>
        public string? TargetRoles { get; set; }

        /// <summary>Generation id, or null for every generation.</summary>
        public int? TargetGenerationId { get; set; }

        /// <summary>Scholar status as an int, or null for every status.</summary>
        public int? TargetStatus { get; set; }

        /// <summary>
        /// When false, inactive accounts are excluded. Defaults false and should stay that
        /// way: an inactive scholar is one who has left, and the platform already suppresses
        /// email to them at dispatch.
        /// </summary>
        public bool IncludeInactive { get; set; }

        // ── Delivery ──────────────────────────────────────────────────────────

        public bool SendEmail { get; set; }
        public bool SendPush { get; set; }

        public string CreatedByUserId { get; set; } = string.Empty;
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Null while still a draft.</summary>
        public DateTime? SentAt { get; set; }

        /// <summary>How many notifications this actually produced.</summary>
        public int RecipientCount { get; set; }

        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    }
}
