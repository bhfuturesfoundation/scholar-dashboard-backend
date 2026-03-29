using Auth.Models.Enums.FLS;

namespace Auth.Models.DTOs.FLS
{
    public class SpeakerProfileDto
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Organization { get; set; }
        public string? Biography { get; set; }
        public SpeakerType SpeakerType { get; set; }
        public string SpeakerTypeLabel { get; set; } = string.Empty;
        public bool IsDeregistered { get; set; }
        public DateTime? DeregisteredAt { get; set; }
        public string? DeregistrationReason { get; set; }
        public bool AccommodationBooked { get; set; }
        public bool FlightTicketBooked { get; set; }
        public DateTime? ModificationDeadline { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<SpeakerUploadDto> Uploads { get; set; } = new();
        public SpeakerCompletionStatusDto CompletionStatus { get; set; } = new();
    }

    public class SpeakerCompletionStatusDto
    {
        public bool CVUploaded { get; set; }
        public bool PictureUploaded { get; set; }
        public bool SynopsisUploaded { get; set; }
        public bool PresentationUploaded { get; set; }
        public bool PresentationVerified { get; set; }

        public UploadStatus CVStatus { get; set; }
        public UploadStatus PictureStatus { get; set; }
        public UploadStatus SynopsisStatus { get; set; }
        public UploadStatus PresentationStatus { get; set; }
    }
}
