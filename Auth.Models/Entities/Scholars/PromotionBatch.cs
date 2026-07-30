using Auth.Models.Enums.Scholars;

namespace Auth.Models.Entities.Scholars
{
    /// <summary>
    /// One run of a bulk promotion, with enough detail to undo it.
    ///
    /// Promotion changes the status of every scholar in a cohort at once. Run against the
    /// wrong generation, or twice, it is not something you can fix by hand across a few
    /// hundred accounts — so each run records exactly who moved and from what, and stays
    /// revertable until someone decides otherwise.
    /// </summary>
    public class PromotionBatch
    {
        public int Id { get; set; }

        public PromotionStep Step { get; set; }

        /// <summary>Null when the run covered every generation.</summary>
        public int? GenerationId { get; set; }
        public ScholarGeneration? Generation { get; set; }

        public int AffectedCount { get; set; }

        /// <summary>Whether promoted alumni were also deactivated.</summary>
        public bool DeactivatedAlumni { get; set; }

        public string PerformedByUserId { get; set; } = string.Empty;
        public string PerformedByName { get; set; } = string.Empty;

        public DateTime PerformedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Set when the batch has been rolled back. A batch can only be reverted once.</summary>
        public DateTime? RevertedAt { get; set; }
        public string? RevertedByUserId { get; set; }

        public bool IsReverted => RevertedAt.HasValue;

        public ICollection<PromotionBatchEntry> Entries { get; set; } = new List<PromotionBatchEntry>();
    }

    /// <summary>
    /// One scholar's movement within a batch. The previous status and active flag are stored
    /// per row rather than derived from the step, because a revert has to restore what each
    /// account actually was — including accounts that were already inactive before the run
    /// and must stay that way.
    /// </summary>
    public class PromotionBatchEntry
    {
        public int Id { get; set; }

        public int PromotionBatchId { get; set; }
        public PromotionBatch PromotionBatch { get; set; } = null!;

        public string UserId { get; set; } = string.Empty;

        /// <summary>Snapshotted so the log stays readable even if the account is later removed.</summary>
        public string UserDisplayName { get; set; } = string.Empty;
        public string? UserEmail { get; set; }

        public ScholarStatus PreviousStatus { get; set; }
        public ScholarStatus NewStatus { get; set; }

        /// <summary>Free-text Title before the change, so a revert restores the display label too.</summary>
        public string? PreviousTitle { get; set; }

        public bool PreviousIsActive { get; set; }
    }
}
