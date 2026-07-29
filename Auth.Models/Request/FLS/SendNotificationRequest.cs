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

        [Required, MaxLength(5000)]
        public string Message { get; set; } = string.Empty;

        public bool SendEmail { get; set; } = true;

        /// <summary>
        /// Optional provider key ("smtp", "gmass", "mailchimp", "resend", "emailjs", "log").
        /// Null uses the configured default.
        /// </summary>
        public string? EmailProvider { get; set; }

        /// <summary>Value substituted for the <c>{{deadline}}</c> placeholder.</summary>
        public string? Deadline { get; set; }
    }
}
