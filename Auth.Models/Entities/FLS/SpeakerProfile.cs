using Auth.Models.Enums.FLS;

namespace Auth.Models.Entities.FLS
{
    public class SpeakerProfile
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        public SpeakerType SpeakerType { get; set; } = SpeakerType.Track;
        public string? Organization { get; set; }
        public string? Biography { get; set; }

        public bool IsDeregistered { get; set; } = false;
        public DateTime? DeregisteredAt { get; set; }
        public string? DeregistrationReason { get; set; }

        // Internal tracking checkboxes
        public bool AccommodationBooked { get; set; } = false;
        public bool FlightTicketBooked { get; set; } = false;

        // Modification deadline - speaker can update uploads until this date
        public DateTime? ModificationDeadline { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<SpeakerUpload> Uploads { get; set; } = new List<SpeakerUpload>();
        public ICollection<MeetingTimeSlot> MeetingSlots { get; set; } = new List<MeetingTimeSlot>();
        public ICollection<SpeakerTask> Tasks { get; set; } = new List<SpeakerTask>();
        public ICollection<SpeakerComment> Comments { get; set; } = new List<SpeakerComment>();
        public ICollection<SpeakerNotification> Notifications { get; set; } = new List<SpeakerNotification>();
    }
}
