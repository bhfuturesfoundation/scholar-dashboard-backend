using Auth.Models.Enums.Mailing;

namespace Auth.Models.Entities.Mailing
{
    /// <summary>
    /// Record of one spreadsheet import. Kept because a bad import is the most likely way
    /// this directory gets corrupted — with a batch id on every firm, "undo the file Amina
    /// uploaded on Tuesday" is answerable.
    /// </summary>
    public class FirmImportBatch
    {
        public int Id { get; set; }

        public string FileName { get; set; } = string.Empty;

        public ImportFormat Format { get; set; }

        public int TotalRows { get; set; }
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int SkippedCount { get; set; }
        public int FailedCount { get; set; }

        /// <summary>
        /// Per-row rejection reasons, newline separated and capped. Full detail is returned
        /// to the browser at import time; this is the persisted summary.
        /// </summary>
        public string? ErrorReport { get; set; }

        /// <summary>
        /// True when the batch was a validation-only pass. Dry runs are recorded too, so a
        /// team member can see that someone checked the file before committing it.
        /// </summary>
        public bool WasDryRun { get; set; }

        public string CreatedByUserId { get; set; } = string.Empty;
        public string CreatedByName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Firm> Firms { get; set; } = new List<Firm>();
    }
}
