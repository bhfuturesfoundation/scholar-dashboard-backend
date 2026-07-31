using Auth.Models.Enums.Notifications;

namespace Auth.Models.Entities.Notifications
{
    /// <summary>
    /// One person's answer to "what may we send you, and how".
    ///
    /// Stored as a row per user rather than a row per category/channel pair: the matrix is
    /// small and fixed, and a single row means loading a preference is one lookup rather
    /// than a fan-out on every send.
    ///
    /// Absence of a row means defaults, so nothing has to be created at registration and an
    /// account made before this feature existed behaves sensibly.
    /// </summary>
    public class NotificationPreference
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        // ── Email ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Journal deadline email. Defaults on, and deliberately the one people are most
        /// likely to want: it is the only notification tied to an obligation.
        /// </summary>
        public bool EmailJournal { get; set; } = true;

        public bool EmailKudos { get; set; } = true;
        public bool EmailAchievements { get; set; }
        public bool EmailAnnouncements { get; set; } = true;
        public bool EmailMentorship { get; set; } = true;

        /// <summary>A weekly roll-up instead of nothing when the per-event switches are off.</summary>
        public bool EmailWeeklyDigest { get; set; } = true;

        // ── Push ──────────────────────────────────────────────────────────────
        //
        // All default OFF. A push subscription only exists because someone deliberately
        // granted permission, but granting permission is not the same as asking for every
        // category — and push is the channel people punish you for.

        public bool PushJournal { get; set; } = true;
        public bool PushKudos { get; set; }
        public bool PushAchievements { get; set; }
        public bool PushAnnouncements { get; set; }
        public bool PushMentorship { get; set; }
        public bool PushMinigames { get; set; } = true;

        // ── Quiet hours ───────────────────────────────────────────────────────

        public bool QuietHoursEnabled { get; set; } = true;

        /// <summary>Local hour quiet time begins. Default 22:00.</summary>
        public int QuietHoursStart { get; set; } = 22;

        /// <summary>Local hour quiet time ends. Default 08:00.</summary>
        public int QuietHoursEnd { get; set; } = 8;

        /// <summary>
        /// IANA identifier, e.g. <c>Europe/Sarajevo</c>. .NET 6+ accepts IANA ids on both
        /// Linux and Windows, so this does not need a per-platform mapping — but an
        /// unrecognised value must never throw during a send, so callers fall back to UTC.
        /// </summary>
        public string TimeZoneId { get; set; } = "Europe/Sarajevo";

        /// <summary>
        /// Which language to write email and push in.
        ///
        /// The in-app bell does not need this — the browser knows what the reader picked and
        /// renders from its own dictionary. Email and push do: nobody is looking at a React
        /// tree at 08:00 when a reminder goes out. The frontend writes this whenever the
        /// language switcher is used, so the two stay in step without the server having to
        /// guess from an Accept-Language header it never sees.
        /// </summary>
        public string PreferredLocale { get; set; } = "bs";

        /// <summary>
        /// Last weekly digest sent, so the digest pass is idempotent without needing a
        /// notification row of its own.
        /// </summary>
        public DateTime? LastDigestAt { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether <paramref name="category"/> may go out on <paramref name="channel"/>.
        ///
        /// <see cref="NotificationCategory.System"/> is always allowed and
        /// <see cref="NotificationChannel.InApp"/> is always allowed: the bell menu is the
        /// record of what happened, and muting it would mean events with no trace at all.
        /// </summary>
        public bool Allows(NotificationCategory category, NotificationChannel channel)
        {
            if (channel == NotificationChannel.InApp) return true;
            if (category == NotificationCategory.System) return true;

            return channel switch
            {
                NotificationChannel.Email => category switch
                {
                    NotificationCategory.Journal => EmailJournal,
                    NotificationCategory.Kudos => EmailKudos,
                    NotificationCategory.Achievement => EmailAchievements,
                    NotificationCategory.Announcement => EmailAnnouncements,
                    NotificationCategory.Mentorship => EmailMentorship,

                    // Minigame invites expire in three minutes. An email about one is
                    // guaranteed to arrive after it is worthless.
                    NotificationCategory.Minigame => false,
                    _ => false
                },

                NotificationChannel.Push => category switch
                {
                    NotificationCategory.Journal => PushJournal,
                    NotificationCategory.Kudos => PushKudos,
                    NotificationCategory.Achievement => PushAchievements,
                    NotificationCategory.Announcement => PushAnnouncements,
                    NotificationCategory.Mentorship => PushMentorship,
                    NotificationCategory.Minigame => PushMinigames,
                    _ => false
                },

                _ => false
            };
        }

        /// <summary>
        /// Whether <paramref name="utcNow"/> falls inside this person's quiet hours.
        ///
        /// Handles the overnight case (22:00–08:00 wraps midnight) as well as a same-day
        /// window. An unparseable time zone means UTC rather than an exception — a bad
        /// preference value must not stop a send.
        /// </summary>
        public bool IsQuietAt(DateTime utcNow)
        {
            if (!QuietHoursEnabled) return false;
            if (QuietHoursStart == QuietHoursEnd) return false;

            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                zone = TimeZoneInfo.Utc;
            }

            var localHour = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone).Hour;

            return QuietHoursStart < QuietHoursEnd
                ? localHour >= QuietHoursStart && localHour < QuietHoursEnd
                : localHour >= QuietHoursStart || localHour < QuietHoursEnd;
        }

        /// <summary>
        /// The next instant outside quiet hours, used as the deferral target. Steps hour by
        /// hour rather than computing the boundary directly so daylight-saving transitions
        /// cannot produce an instant that is still quiet.
        /// </summary>
        public DateTime NextDeliverableInstant(DateTime utcNow)
        {
            if (!IsQuietAt(utcNow)) return utcNow;

            for (var i = 1; i <= 24; i++)
            {
                var candidate = utcNow.AddHours(i);
                if (!IsQuietAt(candidate)) return candidate;
            }

            return utcNow.AddHours(24);
        }
    }
}
