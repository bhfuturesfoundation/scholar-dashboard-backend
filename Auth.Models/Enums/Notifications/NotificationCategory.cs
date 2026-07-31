namespace Auth.Models.Enums.Notifications
{
    /// <summary>
    /// What a notification is about. This is the unit a scholar switches off, so the
    /// granularity is deliberately coarse — "Kudos" rather than "kudos received" and
    /// "kudos hidden by staff". Someone who mutes kudos means all of it.
    ///
    /// Values are persisted as integers, so existing members must never be renumbered.
    /// </summary>
    public enum NotificationCategory
    {
        /// <summary>
        /// Deadlines, submission windows, and confirmation that a journal was received.
        /// Cannot be fully muted for email — see <c>NotificationPreference</c>.
        /// </summary>
        Journal = 0,

        /// <summary>Peer recognition received.</summary>
        Kudos = 1,

        /// <summary>Badges earned.</summary>
        Achievement = 2,

        /// <summary>Staff-authored broadcasts.</summary>
        Announcement = 3,

        /// <summary>Minigame invites and their outcomes. Realtime and short-lived.</summary>
        Minigame = 4,

        /// <summary>
        /// A mentor or programme manager acted on the scholar's work, or the scholar's
        /// standing changed (promotion to Senior, move to Alumni).
        /// </summary>
        Mentorship = 5,

        /// <summary>
        /// Account and security events. Never mutable — a scholar does not get to switch
        /// off being told their password changed.
        /// </summary>
        System = 6
    }
}
