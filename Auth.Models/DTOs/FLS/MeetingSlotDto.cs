namespace Auth.Models.DTOs.FLS
{
    public class MeetingSlotDto
    {
        public int Id { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string OrganizerName { get; set; } = string.Empty;
        public bool IsBooked { get; set; }
        public int? BookedBySpeakerProfileId { get; set; }
        public string? BookedBySpeakerName { get; set; }
        public string? Notes { get; set; }
        public string? MeetingLink { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
