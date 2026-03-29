using Auth.Models.Enums.FLS;
using System.ComponentModel.DataAnnotations;

namespace Auth.Models.Request.FLS
{
    public class SendNotificationRequest
    {
        /// <summary>If null, notification is sent to all active speakers.</summary>
        public int? SpeakerProfileId { get; set; }

        [Required]
        public FLSNotificationType NotificationType { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required, MaxLength(2000)]
        public string Message { get; set; } = string.Empty;

        public bool SendEmail { get; set; } = true;
    }
}
