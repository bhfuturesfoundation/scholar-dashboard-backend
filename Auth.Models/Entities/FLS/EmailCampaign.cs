using Auth.Models.Enums.FLS;

namespace Auth.Models.Entities.FLS
{
    /// <summary>
    /// One bulk send: the message as composed, who it was aimed at, and how it went.
    ///
    /// Persisting campaigns (rather than firing and forgetting) is what makes the partner
    /// dashboard useful — "did the speakers actually get the deadline email?" is otherwise
    /// unanswerable, and a failed send leaves no trace to retry from.
    /// </summary>
    public class EmailCampaign
    {
        public int Id { get; set; }

        public string Subject { get; set; } = string.Empty;

        /// <summary>The message template, placeholders unexpanded.</summary>
        public string Body { get; set; } = string.Empty;

        public CampaignAudience Audience { get; set; }

        /// <summary>Set when <see cref="Audience"/> is <see cref="CampaignAudience.SpeakersByType"/>.</summary>
        public SpeakerType? SpeakerTypeFilter { get; set; }

        /// <summary>
        /// Comma-separated speaker profile ids for
        /// <see cref="CampaignAudience.SelectedSpeakers"/>. Stored denormalised because it
        /// is a point-in-time snapshot of the send, not a live relationship — the recipient
        /// rows are the authoritative record of who was actually mailed.
        /// </summary>
        public string? SelectedSpeakerIds { get; set; }

        /// <summary>Provider key requested at send time. Null means "configured default".</summary>
        public string? ProviderKey { get; set; }

        /// <summary>Value substituted for the {{deadline}} placeholder.</summary>
        public string? Deadline { get; set; }

        public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

        public int TotalRecipients { get; set; }
        public int SentCount { get; set; }
        public int FailedCount { get; set; }

        public string CreatedByUserId { get; set; } = string.Empty;
        public string CreatedByName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        public ICollection<EmailCampaignRecipient> Recipients { get; set; } = new List<EmailCampaignRecipient>();
    }
}
