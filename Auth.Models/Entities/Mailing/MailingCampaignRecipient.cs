using Auth.Models.Enums.FLS;
using Auth.Models.Enums.Mailing;

namespace Auth.Models.Entities.Mailing
{
    /// <summary>
    /// One firm's delivery within a campaign. This is the audit trail: without a row per
    /// recipient, "which firms actually received the January outreach, and which bounced?"
    /// has no answer, and a partial failure can't be retried without re-mailing everyone.
    /// </summary>
    public class MailingCampaignRecipient
    {
        public int Id { get; set; }

        public int CampaignId { get; set; }
        public MailingCampaign Campaign { get; set; } = null!;

        public int FirmId { get; set; }
        public Firm Firm { get; set; } = null!;

        /// <summary>
        /// Address used, snapshotted. If the firm's email is corrected later, the record of
        /// where this message actually went stays accurate.
        /// </summary>
        public string ToEmail { get; set; } = string.Empty;

        /// <summary>Name the message was addressed to — person or firm, per <see cref="VariantUsed"/>.</summary>
        public string? ToName { get; set; }

        public TemplateVariant VariantUsed { get; set; }

        /// <summary>Subject after placeholder expansion, so the log shows what the firm saw.</summary>
        public string? RenderedSubject { get; set; }

        public EmailDeliveryStatus Status { get; set; } = EmailDeliveryStatus.Pending;

        /// <summary>Provider that handled the send — may differ from the campaign's on fallback.</summary>
        public string? ProviderUsed { get; set; }

        public string? ProviderMessageId { get; set; }

        public string? Error { get; set; }

        /// <summary>Incremented on retry, so repeated failures are visible.</summary>
        public int AttemptCount { get; set; }

        public DateTime? SentAt { get; set; }
    }
}
