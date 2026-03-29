using Auth.Models.Enums.FLS;

namespace Auth.Models.DTOs.FLS
{
    public class SpeakerOverviewDto
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Organization { get; set; }
        public SpeakerType SpeakerType { get; set; }
        public string SpeakerTypeLabel { get; set; } = string.Empty;
        public bool IsDeregistered { get; set; }
        public bool AccommodationBooked { get; set; }
        public bool FlightTicketBooked { get; set; }
        public DateTime CreatedAt { get; set; }
        public SpeakerCompletionStatusDto CompletionStatus { get; set; } = new();
        public int PendingNotifications { get; set; }
    }
}
