using Auth.Models.Enums.FLS;

namespace Auth.Models.DTOs.FLS
{
    public class SpeakerNotificationDto
    {
        public int Id { get; set; }
        public FLSNotificationType NotificationType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsRead { get; set; }
        public bool EmailSent { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }
}
