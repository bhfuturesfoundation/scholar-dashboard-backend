using Auth.Models.Enums.Mailing;

namespace Auth.Models.Request.Mailing
{
    public class UpsertFirmGroupRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ColorHex { get; set; }
        public int SortOrder { get; set; }
    }

    public class UpsertFirmTypeRequest
    {
        public string Name { get; set; } = string.Empty;
        public int? FirmGroupId { get; set; }
        public string? Description { get; set; }

        /// <summary>Comma-separated keywords driving auto-categorisation.</summary>
        public string? MatchKeywords { get; set; }

        public string? ColorHex { get; set; }
        public int SortOrder { get; set; }
    }

    public class UpsertFirmRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? LegalName { get; set; }
        public int? FirmTypeId { get; set; }
        public string? Email { get; set; }
        public string? Website { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? ContactPersonName { get; set; }
        public string? ContactPersonRole { get; set; }
        public FirmStatus Status { get; set; } = FirmStatus.Active;
        public string? Notes { get; set; }
    }

    /// <summary>Which firms to run contact-name detection over.</summary>
    public class DetectNamesRequest
    {
        /// <summary>Explicit firm ids. Empty means "every firm matching the filter below".</summary>
        public List<int> FirmIds { get; set; } = new();

        /// <summary>Restrict to one firm type when <see cref="FirmIds"/> is empty.</summary>
        public int? FirmTypeId { get; set; }

        /// <summary>
        /// Include firms that already have a contact name. Off by default: a name someone
        /// typed by hand should not be replaced by a guess.
        /// </summary>
        public bool IncludeFirmsWithNames { get; set; }
    }

    /// <summary>Applies the reviewed results of a bulk name detection.</summary>
    public class ApplyNamesRequest
    {
        public List<ApplyNameItem> Items { get; set; } = new();
    }

    public class ApplyNameItem
    {
        public int FirmId { get; set; }

        /// <summary>The name to store. Null or empty clears it.</summary>
        public string? ContactPersonName { get; set; }

        /// <summary>
        /// True when the operator edited the suggestion. Edited names are stored as Manual
        /// with High confidence, so later automatic runs leave them alone.
        /// </summary>
        public bool WasEdited { get; set; }

        public NameConfidence Confidence { get; set; }
        public ContactNameSource Source { get; set; }
    }

    public class BulkCategorizeRequest
    {
        public List<int> FirmIds { get; set; } = new();

        /// <summary>Apply only whole-word matches automatically. Recommended.</summary>
        public bool ConfidentOnly { get; set; } = true;

        /// <summary>Re-classify firms that already have a type.</summary>
        public bool OverwriteExisting { get; set; }
    }

    public class UpsertTemplateRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? FirmTypeId { get; set; }

        public string SubjectFirmVariant { get; set; } = string.Empty;
        public string BodyFirmVariant { get; set; } = string.Empty;

        public bool PersonVariantEnabled { get; set; }
        public string? SubjectPersonVariant { get; set; }
        public string? BodyPersonVariant { get; set; }

        public bool IsActive { get; set; } = true;
    }

    /// <summary>Audience selection shared by campaigns and schedules.</summary>
    public class AudienceSelection
    {
        public FirmAudience Audience { get; set; } = FirmAudience.AllActive;
        public List<int> FirmTypeIds { get; set; } = new();
        public List<int> FirmGroupIds { get; set; } = new();
        public List<int> FirmIds { get; set; } = new();
    }

    public class PreviewCampaignRequest
    {
        public int TemplateId { get; set; }
        public AudienceSelection Audience { get; set; } = new();

        /// <summary>Values for the template's custom placeholders.</summary>
        public Dictionary<string, string> CustomFields { get; set; } = new();

        /// <summary>How many rendered samples to return.</summary>
        public int SampleSize { get; set; } = 5;
    }

    public class SendMailingCampaignRequest
    {
        public string Name { get; set; } = string.Empty;
        public int TemplateId { get; set; }
        public AudienceSelection Audience { get; set; } = new();
        public Dictionary<string, string> CustomFields { get; set; } = new();

        /// <summary>Null uses the configured default provider.</summary>
        public string? ProviderKey { get; set; }

        /// <summary>
        /// Send to this address only, ignoring the audience. Used by the "send test" button
        /// so the operator sees the real rendered message before mailing hundreds of firms.
        /// </summary>
        public string? TestRecipientEmail { get; set; }
    }

    public class UpsertScheduleRequest
    {
        public string Name { get; set; } = string.Empty;
        public int TemplateId { get; set; }
        public AudienceSelection Audience { get; set; } = new();
        public Dictionary<string, string> CustomFields { get; set; } = new();

        public ScheduleCadence Cadence { get; set; } = ScheduleCadence.FixedInterval;
        public int IntervalMinutes { get; set; } = 1440;
        public DateTime? StartAt { get; set; }

        public bool IsEnabled { get; set; } = true;

        public int BatchSize { get; set; } = 25;
        public int DelayBetweenEmailsMs { get; set; } = 1500;
        public int SendWindowStartHourUtc { get; set; } = 7;
        public int SendWindowEndHourUtc { get; set; } = 17;
        public bool SkipAlreadyContacted { get; set; } = true;
        public int? MaxTotalSends { get; set; }

        public string? ProviderKey { get; set; }
    }
}
