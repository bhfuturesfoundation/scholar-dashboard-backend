using Auth.Models.Enums.FLS;

namespace Auth.Models.DTOs.FLS
{
    public class EmailProviderDto
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsConfigured { get; set; }
        public string? ConfigurationHint { get; set; }
        public bool IsDefault { get; set; }
    }

    /// <summary>Runtime state of the email stack, for the partner settings screen.</summary>
    public class EmailSettingsDto
    {
        public List<EmailProviderDto> Providers { get; set; } = new();
        public string? DefaultProvider { get; set; }
        public bool FallbackEnabled { get; set; }
        public List<string> FallbackOrder { get; set; } = new();

        /// <summary>True when every outbound email is being redirected to a test inbox.</summary>
        public bool SandboxMode { get; set; }
        public string? SandboxRedirectTo { get; set; }

        public int SendDelayMs { get; set; }
        public int MaxRecipientsPerCampaign { get; set; }

        public List<TemplateVariableDto> Variables { get; set; } = new();
    }

    public class TemplateVariableDto
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class CampaignRecipientDto
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? RecipientName { get; set; }
        public int? SpeakerProfileId { get; set; }
        public EmailDeliveryStatus Status { get; set; }
        public string StatusLabel { get; set; } = string.Empty;
        public string? ProviderUsed { get; set; }
        public string? Error { get; set; }
        public DateTime? SentAt { get; set; }
    }

    public class EmailCampaignSummaryDto
    {
        public int Id { get; set; }
        public string Subject { get; set; } = string.Empty;
        public CampaignAudience Audience { get; set; }
        public string AudienceLabel { get; set; } = string.Empty;
        public CampaignStatus Status { get; set; }
        public string StatusLabel { get; set; } = string.Empty;
        public string? ProviderKey { get; set; }
        public int TotalRecipients { get; set; }
        public int SentCount { get; set; }
        public int FailedCount { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }

    public class EmailCampaignDetailDto : EmailCampaignSummaryDto
    {
        public string Body { get; set; } = string.Empty;
        public string? Deadline { get; set; }
        public List<CampaignRecipientDto> Recipients { get; set; } = new();
    }

    /// <summary>
    /// A dry run: who would be mailed and exactly what the first recipient would see,
    /// including any placeholder the template uses but the data can't fill.
    /// </summary>
    public class CampaignPreviewDto
    {
        public int RecipientCount { get; set; }
        public List<string> SampleRecipients { get; set; } = new();

        public string RenderedSubject { get; set; } = string.Empty;
        public string RenderedHtml { get; set; } = string.Empty;
        public string RenderedText { get; set; } = string.Empty;

        /// <summary>Placeholders in the template with no matching value — a send-blocking warning in the UI.</summary>
        public List<string> UnresolvedVariables { get; set; } = new();

        /// <summary>Recipients that will be skipped, with the reason (e.g. missing address).</summary>
        public List<string> Warnings { get; set; } = new();

        public string? ProviderKey { get; set; }
        public bool SandboxMode { get; set; }
    }

    /// <summary>One addressable person in the recipient directory.</summary>
    public class DirectoryRecipientDto
    {
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public int? SpeakerProfileId { get; set; }

        /// <summary>"Speaker" or the staff role name.</summary>
        public string Kind { get; set; } = string.Empty;

        public string? Organization { get; set; }
        public string? SpeakerType { get; set; }

        /// <summary>Speakers only — true when at least one required upload is missing.</summary>
        public bool HasIncompleteUploads { get; set; }

        public bool IsDeregistered { get; set; }
    }
}
