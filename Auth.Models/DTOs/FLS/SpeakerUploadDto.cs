using Auth.Models.Enums.FLS;

namespace Auth.Models.DTOs.FLS
{
    public class SpeakerUploadDto
    {
        public int Id { get; set; }
        public UploadType UploadType { get; set; }
        public string UploadTypeLabel { get; set; } = string.Empty;
        public UploadStatus Status { get; set; }
        public string StatusLabel { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public int? WordCount { get; set; }
        public string? VerificationNotes { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime UploadedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
