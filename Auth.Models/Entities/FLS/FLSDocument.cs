using Auth.Models.Enums.FLS;

namespace Auth.Models.Entities.FLS
{
    public class FLSDocument
    {
        public int Id { get; set; }

        public FLSDocumentType DocumentType { get; set; }
        public string Title { get; set; } = string.Empty;

        public string OriginalFileName { get; set; } = string.Empty;
        public string StoredFileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string MimeType { get; set; } = string.Empty;

        public string UploadedByUserId { get; set; } = string.Empty;

        /// <summary>If null, document is for all speakers. If set, it's speaker-specific (e.g., marketing visual).</summary>
        public int? TargetSpeakerProfileId { get; set; }
        public SpeakerProfile? TargetSpeaker { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<SpeakerComment> Comments { get; set; } = new List<SpeakerComment>();
    }
}
