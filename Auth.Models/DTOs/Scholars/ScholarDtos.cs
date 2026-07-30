using Auth.Models.Enums.Scholars;

namespace Auth.Models.DTOs.Scholars
{
    public class ScholarGenerationDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Year { get; set; }
        public string? Description { get; set; }
        public DateTime? StartsOn { get; set; }
        public DateTime? EndsOn { get; set; }
        public bool IsCurrent { get; set; }
        public DateTime CreatedAt { get; set; }

        public int TotalScholars { get; set; }
        public int JuniorCount { get; set; }
        public int SeniorCount { get; set; }
        public int AlumniCount { get; set; }
    }

    public class ScholarStatusCountDto
    {
        public ScholarStatus Status { get; set; }
        public string Label { get; set; } = string.Empty;
        public int Count { get; set; }
        public int ActiveCount { get; set; }
    }

    public class ScholarOverviewDto
    {
        public int TotalScholars { get; set; }
        public List<ScholarStatusCountDto> ByStatus { get; set; } = new();
        public List<ScholarGenerationDto> Generations { get; set; } = new();

        /// <summary>
        /// Accounts with no cohort. Surfaced deliberately: promotion skips them, so leaving
        /// them invisible would silently exclude people from the yearly roll-over.
        /// </summary>
        public int UngeneratedCount { get; set; }

        /// <summary>Accounts whose historic Title could not be mapped to a status.</summary>
        public int UnassignedStatusCount { get; set; }
    }

    public class PromotionCandidateDto
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? GenerationName { get; set; }
        public ScholarStatus CurrentStatus { get; set; }
        public ScholarStatus NewStatus { get; set; }
        public bool IsActive { get; set; }
    }

    public class PromotionPreviewDto
    {
        public PromotionStep Step { get; set; }
        public string StepLabel { get; set; } = string.Empty;
        public int AffectedCount { get; set; }
        public string? GenerationName { get; set; }

        /// <summary>Whether alumni would also be deactivated by this run.</summary>
        public bool WillDeactivate { get; set; }

        /// <summary>A sample of who moves — not the whole list.</summary>
        public List<PromotionCandidateDto> Samples { get; set; } = new();

        /// <summary>Plain-language description of the effect, shown in the confirm dialog.</summary>
        public string Summary { get; set; } = string.Empty;
    }

    public class PromotionResultDto
    {
        public int BatchId { get; set; }
        public int AffectedCount { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class PromotionBatchDto
    {
        public int Id { get; set; }
        public PromotionStep Step { get; set; }
        public string StepLabel { get; set; } = string.Empty;
        public string? GenerationName { get; set; }
        public int AffectedCount { get; set; }
        public bool DeactivatedAlumni { get; set; }
        public string PerformedByName { get; set; } = string.Empty;
        public DateTime PerformedAt { get; set; }
        public bool IsReverted { get; set; }
        public DateTime? RevertedAt { get; set; }
    }

    /// <summary>One created account plus its generated password — returned exactly once.</summary>
    public class CreatedScholarCredentialDto
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Plain-text, and the only time it exists. Only the hash is stored, so this cannot
        /// be retrieved later — it is shown once and must be handed over then.
        /// </summary>
        public string TemporaryPassword { get; set; } = string.Empty;
    }

    public class ScholarImportRowIssueDto
    {
        public int RowNumber { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string Outcome { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class ScholarImportResultDto
    {
        public bool WasDryRun { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string? GenerationName { get; set; }

        public int TotalRows { get; set; }
        public int CreatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }

        public List<string> DetectedColumns { get; set; } = new();
        public List<ScholarImportRowIssueDto> Issues { get; set; } = new();

        /// <summary>Populated only on a committed run. Empty for a dry run.</summary>
        public List<CreatedScholarCredentialDto> Credentials { get; set; } = new();

        /// <summary>True when the credential sheet was also archived to Dropbox.</summary>
        public bool CredentialsArchived { get; set; }
    }

    // ── Mentor assignment ─────────────────────────────────────────────────────

    public class MentorSummaryDto
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public bool IsActive { get; set; }
        public int MenteeCount { get; set; }
    }

    public class MenteeAssignmentDto
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? GenerationName { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        public string? MentorId { get; set; }
        public string? MentorName { get; set; }
        public string? MentorEmail { get; set; }
    }

    public class MentorAssignmentOverviewDto
    {
        public int TotalScholars { get; set; }
        public int AssignedCount { get; set; }
        public int UnassignedCount { get; set; }
        public int MentorCount { get; set; }

        /// <summary>Mentors carrying no mentees — spare capacity.</summary>
        public int MentorsWithNoMentees { get; set; }

        /// <summary>Largest single caseload, for spotting an overloaded mentor.</summary>
        public int LargestCaseload { get; set; }
    }

    public class MentorPairingIssueDto
    {
        public int RowNumber { get; set; }
        public string? MentorEmail { get; set; }
        public string? ScholarEmail { get; set; }
        public string Outcome { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class MentorPairingResultDto
    {
        public bool WasDryRun { get; set; }
        public string FileName { get; set; } = string.Empty;

        public int TotalRows { get; set; }
        public int AssignedCount { get; set; }
        public int ReassignedCount { get; set; }
        public int UnchangedCount { get; set; }
        public int FailedCount { get; set; }

        public List<string> DetectedColumns { get; set; } = new();

        /// <summary>
        /// Every row that could not be paired, with the reason. Surfaced to the operator
        /// rather than written to a startup log.
        /// </summary>
        public List<MentorPairingIssueDto> Issues { get; set; } = new();
    }
}
