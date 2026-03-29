namespace Auth.Models.Entities.FLS
{
    public class MeetingTimeSlot
    {
        public int Id { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        /// <summary>Name of the organizer (Amina, Lejla, Emina, Kajs)</summary>
        public string OrganizerName { get; set; } = string.Empty;

        public bool IsBooked { get; set; } = false;

        public int? BookedBySpeakerProfileId { get; set; }
        public SpeakerProfile? BookedBySpeaker { get; set; }

        public string? Notes { get; set; }
        public string? MeetingLink { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
