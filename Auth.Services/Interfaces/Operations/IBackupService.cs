using Auth.Models.Entities.Operations;
using Auth.Models.Enums.Operations;

namespace Auth.Services.Interfaces.Operations
{
    public class CreateBackupRequest
    {
        public BackupFormat Format { get; set; } = BackupFormat.Json;

        /// <summary>
        /// Include password hashes, refresh tokens and 2FA secrets.
        ///
        /// Off by default and deliberately awkward to turn on. With it off the artefact is
        /// still a complete restore of business data; with it on the file becomes a
        /// credential store, and anywhere it is subsequently copied inherits that.
        /// </summary>
        public bool IncludeSensitiveData { get; set; }

        /// <summary>Upload to Dropbox for retention as well as returning it for download.</summary>
        public bool ArchiveToDropbox { get; set; } = true;

        /// <summary>Days to keep before pruning. Null keeps indefinitely.</summary>
        public int? RetentionDays { get; set; } = 30;
    }

    /// <summary>A produced backup: the record plus the bytes, when they're still in hand.</summary>
    public class BackupArtifact
    {
        public BackupRecord Record { get; init; } = null!;
        public byte[] Content { get; init; } = Array.Empty<byte>();
        public string ContentType { get; init; } = "application/octet-stream";
    }

    /// <summary>Whether a format can actually be produced in this environment.</summary>
    public class BackupFormatAvailability
    {
        public BackupFormat Format { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public bool IsAvailable { get; init; }
        public string? UnavailableReason { get; init; }

        /// <summary>How to restore from this format — shown next to the download button.</summary>
        public string RestoreInstructions { get; init; } = string.Empty;
    }

    /// <summary>
    /// Produces database backups on demand and on a schedule.
    ///
    /// Admin-only at the controller. A backup contains every scholar's journal entries —
    /// personal reflections written in confidence — so the ability to produce one is the most
    /// sensitive permission in the system.
    /// </summary>
    public interface IBackupService
    {
        Task<BackupArtifact> CreateAsync(
            CreateBackupRequest request,
            string? userId,
            string userName,
            CancellationToken cancellationToken = default);

        Task<List<BackupRecord>> GetHistoryAsync(int limit = 50, CancellationToken cancellationToken = default);

        /// <summary>Which formats this deployment can actually produce, and why not if not.</summary>
        Task<List<BackupFormatAvailability>> GetFormatAvailabilityAsync(CancellationToken cancellationToken = default);

        /// <summary>Deletes expired records and their archived artefacts. Returns the count pruned.</summary>
        Task<int> PruneExpiredAsync(CancellationToken cancellationToken = default);
    }
}
