using Auth.Models.Enums.FLS;

namespace Auth.Models.Entities.FLS
{
    /// <summary>
    /// Per-recipient delivery record for a campaign — the audit trail that answers
    /// "did this specific person get it, via which provider, and if not, why not?".
    /// </summary>
    public class EmailCampaignRecipient
    {
        public int Id { get; set; }

        public int EmailCampaignId { get; set; }
        public EmailCampaign EmailCampaign { get; set; } = null!;

        /// <summary>
        /// Address as it was at send time. Deliberately copied rather than joined: if the
        /// user later changes their email, the history must still show where it went.
        /// </summary>
        public string Email { get; set; } = string.Empty;

        public string? RecipientName { get; set; }

        /// <summary>Identity user id, when the recipient is a platform account.</summary>
        public string? UserId { get; set; }

        /// <summary>Speaker profile id, when the recipient is a speaker.</summary>
        public int? SpeakerProfileId { get; set; }

        public EmailDeliveryStatus Status { get; set; } = EmailDeliveryStatus.Pending;

        /// <summary>Provider that handled the send — may differ from the campaign's requested provider when fallback kicks in.</summary>
        public string? ProviderUsed { get; set; }

        public string? ProviderMessageId { get; set; }

        public string? Error { get; set; }

        public DateTime? SentAt { get; set; }
    }
}
