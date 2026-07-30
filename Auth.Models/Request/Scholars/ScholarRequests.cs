using Auth.Models.Enums.Scholars;

namespace Auth.Models.Request.Scholars
{
    public class UpsertGenerationRequest
    {
        public string Name { get; set; } = string.Empty;
        public int Year { get; set; }
        public string? Description { get; set; }
        public DateTime? StartsOn { get; set; }
        public DateTime? EndsOn { get; set; }

        /// <summary>Make this the default cohort for new intake, clearing the previous one.</summary>
        public bool IsCurrent { get; set; }
    }

    public class PromotionRequest
    {
        public PromotionStep Step { get; set; }

        /// <summary>Restrict to one cohort. Null promotes every matching scholar.</summary>
        public int? GenerationId { get; set; }

        /// <summary>
        /// Also deactivate the scholars who become alumni. Only meaningful for
        /// <see cref="PromotionStep.SeniorsToAlumni"/>.
        ///
        /// Off by default: deactivating is what stops an account logging in AND silences all
        /// email to it, so it should be a deliberate choice rather than a side effect of the
        /// yearly roll-over.
        /// </summary>
        public bool DeactivateAlumni { get; set; }

        /// <summary>How many sample rows the preview returns.</summary>
        public int SampleSize { get; set; } = 10;
    }

    public class SetScholarStatusRequest
    {
        public List<string> UserIds { get; set; } = new();
        public ScholarStatus Status { get; set; }

        /// <summary>Optionally move them to a cohort at the same time.</summary>
        public int? GenerationId { get; set; }
    }

    public class ScholarImportOptions
    {
        /// <summary>Validate and report without creating anything. Default.</summary>
        public bool DryRun { get; set; } = true;

        /// <summary>Cohort the new scholars join. Falls back to the current generation.</summary>
        public int? GenerationId { get; set; }

        /// <summary>Status to create them with. Intake is normally Junior.</summary>
        public ScholarStatus Status { get; set; } = ScholarStatus.Junior;

        /// <summary>
        /// Upload the generated credential sheet to Dropbox as well as returning it.
        /// Skipped silently when Dropbox isn't configured.
        /// </summary>
        public bool ArchiveCredentials { get; set; } = true;
    }
}
