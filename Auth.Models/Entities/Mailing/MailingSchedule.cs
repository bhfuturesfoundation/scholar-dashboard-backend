using Auth.Models.Enums.Mailing;

namespace Auth.Models.Entities.Mailing
{
    /// <summary>
    /// A recurring or deferred send. The scheduler wakes up, finds due schedules, and
    /// materialises a <see cref="MailingCampaign"/> for each — so an automated send leaves
    /// exactly the same audit trail as a manual one.
    ///
    /// The batching and window fields exist for deliverability, not convenience: mailing
    /// 400 firms in one burst at 03:00 is the fastest way into a spam folder. Small batches
    /// inside business hours, spaced out, look like a human sending mail.
    /// </summary>
    public class MailingSchedule
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int TemplateId { get; set; }
        public MailingTemplate Template { get; set; } = null!;

        // ── Audience (same selectors as a campaign) ───────────────────────────────

        public FirmAudience Audience { get; set; }
        public string? FirmTypeIds { get; set; }
        public string? FirmGroupIds { get; set; }
        public string? SelectedFirmIds { get; set; }

        // ── Cadence ───────────────────────────────────────────────────────────────

        public ScheduleCadence Cadence { get; set; }

        /// <summary>Gap between runs for <see cref="ScheduleCadence.FixedInterval"/>. Minimum 15.</summary>
        public int IntervalMinutes { get; set; } = 60;

        /// <summary>Next due time, UTC. Null disables the schedule regardless of <see cref="IsEnabled"/>.</summary>
        public DateTime? NextRunAt { get; set; }

        public DateTime? LastRunAt { get; set; }

        /// <summary>Campaign produced by the most recent run, for a one-click jump to results.</summary>
        public int? LastCampaignId { get; set; }

        public bool IsEnabled { get; set; } = true;

        // ── Deliverability controls ───────────────────────────────────────────────

        /// <summary>Firms mailed per run. Keeps each burst small enough to look human.</summary>
        public int BatchSize { get; set; } = 25;

        /// <summary>
        /// Pause between individual sends within a run, milliseconds. Also keeps free-tier
        /// providers (EmailJS, GMass) from rate-limiting the batch.
        /// </summary>
        public int DelayBetweenEmailsMs { get; set; } = 1500;

        /// <summary>Earliest hour (UTC, 0-23) a run may start. Runs due outside the window wait.</summary>
        public int SendWindowStartHourUtc { get; set; } = 7;

        /// <summary>Latest hour (UTC, 0-23) a run may start.</summary>
        public int SendWindowEndHourUtc { get; set; } = 17;

        /// <summary>
        /// Skip firms already contacted by an earlier run of THIS schedule. This is what
        /// turns a recurring schedule into a work-through-the-list drip rather than mailing
        /// the same firms over and over.
        /// </summary>
        public bool SkipAlreadyContacted { get; set; } = true;

        /// <summary>Stop after this many total sends. Null means no cap.</summary>
        public int? MaxTotalSends { get; set; }

        public int TotalSent { get; set; }

        /// <summary>Provider key for every run. Null means "configured default".</summary>
        public string? ProviderKey { get; set; }

        public string? CustomFieldsJson { get; set; }

        public string CreatedByUserId { get; set; } = string.Empty;
        public string CreatedByName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Last failure from the scheduler, surfaced in the UI so silent breakage is visible.</summary>
        public string? LastError { get; set; }

        public ICollection<MailingCampaign> Campaigns { get; set; } = new List<MailingCampaign>();

        /// <summary>Whether <paramref name="utcNow"/> falls inside the configured send window.</summary>
        public bool IsWithinSendWindow(DateTime utcNow)
        {
            // Equal start and end means "no restriction" — otherwise the window would be
            // a single instant and the schedule would never fire.
            if (SendWindowStartHourUtc == SendWindowEndHourUtc) return true;

            var hour = utcNow.Hour;

            // A window that wraps midnight (e.g. 22 → 04) is inclusive of both ends of the day.
            return SendWindowStartHourUtc < SendWindowEndHourUtc
                ? hour >= SendWindowStartHourUtc && hour < SendWindowEndHourUtc
                : hour >= SendWindowStartHourUtc || hour < SendWindowEndHourUtc;
        }

        /// <summary>Whether the total-send cap has been reached.</summary>
        public bool HasReachedCap => MaxTotalSends.HasValue && TotalSent >= MaxTotalSends.Value;

        /// <summary>Next due time after a run, or null when the schedule is finished.</summary>
        public DateTime? ComputeNextRun(DateTime from) => Cadence switch
        {
            ScheduleCadence.Once => null,
            ScheduleCadence.FixedInterval => from.AddMinutes(Math.Max(15, IntervalMinutes)),
            ScheduleCadence.Daily => from.AddDays(1),
            ScheduleCadence.Weekly => from.AddDays(7),
            ScheduleCadence.Monthly => from.AddMonths(1),
            _ => null
        };
    }
}
