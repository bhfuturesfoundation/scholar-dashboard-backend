using System.ComponentModel.DataAnnotations;

namespace Auth.Models.Request.FLS
{
    public class CreateMeetingSlotRequest
    {
        [Required]
        public DateTime StartTime { get; set; }

        [Required]
        public DateTime EndTime { get; set; }

        [Required]
        public string OrganizerName { get; set; } = string.Empty;

        public string? Notes { get; set; }
        public string? MeetingLink { get; set; }
    }

    public class BookMeetingSlotRequest
    {
        public string? Notes { get; set; }
    }
}
