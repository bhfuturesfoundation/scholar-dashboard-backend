using Auth.Models.Enums.FLS;

namespace Auth.Models.DTOs.FLS
{
    public class FLSDocumentDto
    {
        public int Id { get; set; }
        public FLSDocumentType DocumentType { get; set; }
        public string DocumentTypeLabel { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string UploadedByUserId { get; set; } = string.Empty;
        public int? TargetSpeakerProfileId { get; set; }
        public string? TargetSpeakerName { get; set; }
        public bool IsActive { get; set; }
        public DateTime UploadedAt { get; set; }
        public int CommentCount { get; set; }
        public List<SpeakerCommentDto> Comments { get; set; } = new();
    }

    public class SpeakerCommentDto
    {
        public int Id { get; set; }
        public int SpeakerProfileId { get; set; }
        public string SpeakerName { get; set; } = string.Empty;
        public int FLSDocumentId { get; set; }
        public bool IsApproval { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
