using Auth.Models.Enums.Operations;

namespace Auth.Models.Entities.Operations
{
    /// <summary>
    /// One database backup: what was taken, by whom, where it went, and whether it worked.
    ///
    /// The record is written before the artefact is produced and updated afterwards, so a
    /// backup that crashes mid-run leaves a Failed row rather than no trace at all. A backup
    /// system that fails silently is worse than none, because it produces false confidence.
    /// </summary>
    public class BackupRecord
    {
        public int Id { get; set; }

        public string FileName { get; set; } = string.Empty;

        public BackupFormat Format { get; set; }

        public BackupStatus Status { get; set; } = BackupStatus.Running;

        public long SizeBytes { get; set; }

        public int TableCount { get; set; }
        public int RowCount { get; set; }

        /// <summary>
        /// Whether password hashes, refresh tokens and 2FA secrets were included.
        /// Off by default; turning it on requires an explicit opt-in and is recorded here so
        /// the existence of a credential-bearing artefact is always auditable.
        /// </summary>
        public bool IncludesSensitiveData { get; set; }

        /// <summary>Dropbox path when the artefact was uploaded, null when download-only.</summary>
        public string? StoragePath { get; set; }

        /// <summary>True when the artefact was uploaded to Dropbox for retention.</summary>
        public bool IsArchived { get; set; }

        public string? Error { get; set; }

        /// <summary>Null for scheduled backups, which have no human initiator.</summary>
        public string? CreatedByUserId { get; set; }
        public string CreatedByName { get; set; } = string.Empty;

        /// <summary>True when produced by the scheduled job rather than a person.</summary>
        public bool IsAutomatic { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// When retention prunes this. Null means keep indefinitely.
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - StartedAt : null;
    }
}
