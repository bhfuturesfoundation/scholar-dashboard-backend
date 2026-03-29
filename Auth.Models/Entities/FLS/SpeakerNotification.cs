using Auth.Models.Enums.FLS;

namespace Auth.Models.Entities.FLS
{
    public class SpeakerNotification
    {
        public int Id { get; set; }
        public int SpeakerProfileId { get; set; }
        public SpeakerProfile SpeakerProfile { get; set; } = null!;

        public FLSNotificationType NotificationType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        public bool IsRead { get; set; } = false;
        public bool EmailSent { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReadAt { get; set; }
    }
}
