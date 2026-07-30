using Auth.Models.Enums.Mailing;

namespace Auth.Models.Entities.Mailing
{
    /// <summary>
    /// One outreach send to firms: the message as it was composed, who it targeted, and
    /// how each delivery went.
    ///
    /// The template's text is snapshotted onto the campaign rather than referenced, so
    /// editing a template later never rewrites the history of what was actually sent.
    /// </summary>
    public class MailingCampaign
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Template the message came from, kept for reporting. May be null for ad-hoc sends.</summary>
        public int? TemplateId { get; set; }
        public MailingTemplate? Template { get; set; }

        // ── Snapshot of the message at send time ──────────────────────────────────

        public string SubjectFirmVariant { get; set; } = string.Empty;
        public string BodyFirmVariant { get; set; } = string.Empty;
        public bool PersonVariantEnabled { get; set; }
        public string? SubjectPersonVariant { get; set; }
        public string? BodyPersonVariant { get; set; }

        // ── Audience ──────────────────────────────────────────────────────────────

        public FirmAudience Audience { get; set; }

        /// <summary>Comma-separated firm type ids for <see cref="FirmAudience.ByFirmType"/>.</summary>
        public string? FirmTypeIds { get; set; }

        /// <summary>Comma-separated firm group ids for <see cref="FirmAudience.ByFirmGroup"/>.</summary>
        public string? FirmGroupIds { get; set; }

        /// <summary>
        /// Comma-separated firm ids for <see cref="FirmAudience.SelectedFirms"/>. Denormalised
        /// because it is a point-in-time snapshot; the recipient rows are the authoritative
        /// record of who was actually mailed.
        /// </summary>
        public string? SelectedFirmIds { get; set; }

        // ── Send configuration ────────────────────────────────────────────────────

        /// <summary>Provider key requested at send time. Null means "configured default".</summary>
        public string? ProviderKey { get; set; }

        /// <summary>
        /// User-supplied values for the template's custom placeholders, as a JSON object.
        /// Stored as JSON rather than columns because the placeholder set is defined by
        /// whoever writes the template, not by this schema.
        /// </summary>
        public string? CustomFieldsJson { get; set; }

        /// <summary>Schedule that produced this campaign, when it wasn't sent by hand.</summary>
        public int? ScheduleId { get; set; }
        public MailingSchedule? Schedule { get; set; }

        public MailingCampaignStatus Status { get; set; } = MailingCampaignStatus.Draft;

        public int TotalRecipients { get; set; }
        public int SentCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }

        public string CreatedByUserId { get; set; } = string.Empty;
        public string CreatedByName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public ICollection<MailingCampaignRecipient> Recipients { get; set; } = new List<MailingCampaignRecipient>();
    }
}
