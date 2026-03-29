using Auth.Models.Enums.FLS;

namespace Auth.Models.Entities.FLS
{
    public class SpeakerUpload
    {
        public int Id { get; set; }
        public int SpeakerProfileId { get; set; }
        public SpeakerProfile SpeakerProfile { get; set; } = null!;

        public UploadType UploadType { get; set; }
        public UploadStatus Status { get; set; } = UploadStatus.Pending;

        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string MimeType { get; set; } = string.Empty;

        // For synopsis: word count tracking
        public int? WordCount { get; set; }

        // Admin verification
        public string? VerifiedByUserId { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public string? VerificationNotes { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
