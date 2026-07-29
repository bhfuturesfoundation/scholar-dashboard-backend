using Auth.Models.Enums.FLS;
using System.ComponentModel.DataAnnotations;

namespace Auth.Models.Request.FLS
{
    public class SendCampaignRequest
    {
        [Required, MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required, MaxLength(10000)]
        public string Body { get; set; } = string.Empty;

        [Required]
        public CampaignAudience Audience { get; set; }

        /// <summary>Required when <see cref="Audience"/> is <c>SpeakersByType</c>.</summary>
        public SpeakerType? SpeakerTypeFilter { get; set; }

        /// <summary>Required when <see cref="Audience"/> is <c>SelectedSpeakers</c>.</summary>
        public List<int>? SpeakerProfileIds { get; set; }

        /// <summary>Provider key. Null uses the configured default.</summary>
        public string? ProviderKey { get; set; }

        /// <summary>Value substituted for <c>{{deadline}}</c>.</summary>
        public string? Deadline { get; set; }

        /// <summary>
        /// Also create an in-app notification for speaker recipients, so the message is
        /// visible in the portal even if the email is missed or filtered.
        /// </summary>
        public bool AlsoCreateInAppNotification { get; set; } = true;
    }

    public class PreviewCampaignRequest : SendCampaignRequest
    {
    }
}
