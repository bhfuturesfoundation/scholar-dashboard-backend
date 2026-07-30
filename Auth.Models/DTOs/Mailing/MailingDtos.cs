using Auth.Models.Enums.Mailing;

namespace Auth.Models.DTOs.Mailing
{
    public class FirmGroupDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ColorHex { get; set; }
        public int SortOrder { get; set; }
        public bool IsSystem { get; set; }
        public int FirmTypeCount { get; set; }
        public int FirmCount { get; set; }
    }

    public class FirmTypeDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public int? FirmGroupId { get; set; }
        public string? FirmGroupName { get; set; }
        public string? Description { get; set; }
        public string? MatchKeywords { get; set; }
        public string? ColorHex { get; set; }
        public int SortOrder { get; set; }
        public bool IsSystem { get; set; }
        public int FirmCount { get; set; }
        public int TemplateCount { get; set; }
    }

    public class FirmDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? LegalName { get; set; }
        public int? FirmTypeId { get; set; }
        public string? FirmTypeName { get; set; }
        public string? FirmGroupName { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? Phone { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? ContactPersonName { get; set; }
        public string? ContactPersonRole { get; set; }
        public ContactNameSource ContactNameSource { get; set; }
        public NameConfidence ContactNameConfidence { get; set; }
        public FirmStatus Status { get; set; }
        public string? Notes { get; set; }
        public DateTime? LastContactedAt { get; set; }
        public int ContactCount { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Whether the person-variant template would be used for this firm.</summary>
        public bool HasUsableContactName { get; set; }
    }

    /// <summary>One firm's proposed contact name, for the bulk-detect review table.</summary>
    public class NameDetectionResultDto
    {
        public int FirmId { get; set; }
        public string FirmName { get; set; } = string.Empty;
        public string? Email { get; set; }

        public string? CurrentName { get; set; }
        public string? SuggestedName { get; set; }

        public NameConfidence Confidence { get; set; }
        public ContactNameSource Source { get; set; }

        /// <summary>Why this is the suggestion — or why there isn't one.</summary>
        public string? Reason { get; set; }

        /// <summary>
        /// Whether this row is ticked by default. False for low-confidence suggestions and
        /// for firms whose name was set by a human.
        /// </summary>
        public bool SelectedByDefault { get; set; }

        /// <summary>True when a person already set this name — automatic detection won't overwrite it.</summary>
        public bool IsManuallySet { get; set; }
    }

    public class MailingTemplateDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? FirmTypeId { get; set; }
        public string? FirmTypeName { get; set; }

        public string SubjectFirmVariant { get; set; } = string.Empty;
        public string BodyFirmVariant { get; set; } = string.Empty;

        public bool PersonVariantEnabled { get; set; }
        public string? SubjectPersonVariant { get; set; }
        public string? BodyPersonVariant { get; set; }

        public bool IsActive { get; set; }
        public bool SupportsPersonVariant { get; set; }

        /// <summary>Placeholders found across both variants, for the field editor.</summary>
        public List<string> Variables { get; set; } = new();

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A single rendered preview for one firm.</summary>
    public class CampaignPreviewItemDto
    {
        public int FirmId { get; set; }
        public string FirmName { get; set; } = string.Empty;
        public string? ToEmail { get; set; }
        public string? ToName { get; set; }
        public TemplateVariant VariantUsed { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string HtmlBody { get; set; } = string.Empty;

        /// <summary>Placeholders with no value — the campaign is blocked while any remain.</summary>
        public List<string> UnresolvedVariables { get; set; } = new();

        public bool IsSuppressed { get; set; }
        public string? SuppressionReason { get; set; }
    }

    public class CampaignPreviewDto
    {
        public int TotalMatched { get; set; }
        public int Sendable { get; set; }
        public int SuppressedCount { get; set; }
        public int PersonVariantCount { get; set; }
        public int FirmVariantCount { get; set; }

        /// <summary>Every unresolved placeholder across the audience, deduplicated.</summary>
        public List<string> UnresolvedVariables { get; set; } = new();

        /// <summary>A sample of rendered messages — not the whole audience.</summary>
        public List<CampaignPreviewItemDto> Samples { get; set; } = new();
    }

    public class MailingCampaignDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? TemplateId { get; set; }
        public string? TemplateName { get; set; }
        public FirmAudience Audience { get; set; }
        public MailingCampaignStatus Status { get; set; }
        public string? ProviderKey { get; set; }
        public int TotalRecipients { get; set; }
        public int SentCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public string CreatedByName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool WasScheduled { get; set; }
    }

    public class MailingCampaignRecipientDto
    {
        public int Id { get; set; }
        public int FirmId { get; set; }
        public string FirmName { get; set; } = string.Empty;
        public string ToEmail { get; set; } = string.Empty;
        public string? ToName { get; set; }
        public TemplateVariant VariantUsed { get; set; }
        public string? RenderedSubject { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ProviderUsed { get; set; }
        public string? Error { get; set; }
        public int AttemptCount { get; set; }
        public DateTime? SentAt { get; set; }
    }

    public class MailingScheduleDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TemplateId { get; set; }
        public string TemplateName { get; set; } = string.Empty;
        public FirmAudience Audience { get; set; }
        public ScheduleCadence Cadence { get; set; }
        public int IntervalMinutes { get; set; }
        public DateTime? NextRunAt { get; set; }
        public DateTime? LastRunAt { get; set; }
        public bool IsEnabled { get; set; }
        public int BatchSize { get; set; }
        public int DelayBetweenEmailsMs { get; set; }
        public int SendWindowStartHourUtc { get; set; }
        public int SendWindowEndHourUtc { get; set; }
        public bool SkipAlreadyContacted { get; set; }
        public int? MaxTotalSends { get; set; }
        public int TotalSent { get; set; }
        public string? ProviderKey { get; set; }
        public string? LastError { get; set; }
        public string CreatedByName { get; set; } = string.Empty;

        /// <summary>How many firms the audience currently resolves to.</summary>
        public int AudienceSize { get; set; }
    }

    /// <summary>Outcome of a firm import, returned for both dry runs and real runs.</summary>
    public class FirmImportResultDto
    {
        public int BatchId { get; set; }
        public bool WasDryRun { get; set; }
        public string FileName { get; set; } = string.Empty;

        public int TotalRows { get; set; }
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }

        public int AutoCategorizedCount { get; set; }
        public int NamesDetectedCount { get; set; }

        /// <summary>Per-row problems, capped so a broken file can't return megabytes.</summary>
        public List<FirmImportRowIssueDto> Issues { get; set; } = new();

        /// <summary>Headers found in the file, so the UI can show what was mapped.</summary>
        public List<string> DetectedColumns { get; set; } = new();
    }

    public class FirmImportRowIssueDto
    {
        public int RowNumber { get; set; }
        public string? FirmName { get; set; }
        public string? Email { get; set; }
        public string Outcome { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
