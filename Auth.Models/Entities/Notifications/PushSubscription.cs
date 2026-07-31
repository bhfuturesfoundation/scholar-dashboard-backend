namespace Auth.Models.Entities.Notifications
{
    /// <summary>
    /// One browser or installed PWA that has granted push permission.
    ///
    /// A person has as many of these as they have devices, which is the point: the whole
    /// reason to add push is to reach someone whose laptop is shut.
    /// </summary>
    public class PushSubscription
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        /// <summary>
        /// The push service endpoint issued by the browser vendor. Unique across all users:
        /// if the same physical browser is later signed in as somebody else, the old row is
        /// reassigned rather than duplicated, otherwise the previous account's notifications
        /// would keep arriving on a device that no longer belongs to them.
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>Client public key (base64url), from <c>subscription.getKey("p256dh")</c>.</summary>
        public string P256dh { get; set; } = string.Empty;

        /// <summary>Shared auth secret (base64url), from <c>subscription.getKey("auth")</c>.</summary>
        public string Auth { get; set; } = string.Empty;

        /// <summary>Free-form, for the "your devices" list. Never trusted, only displayed.</summary>
        public string? UserAgent { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Last time this subscription accepted a push.</summary>
        public DateTime? LastSuccessAt { get; set; }

        /// <summary>
        /// Consecutive delivery failures. A subscription that the push service rejects with
        /// 404 or 410 is gone for good and is deleted immediately; this counter exists for
        /// the softer failures, so a transiently unreachable device is not dropped on one
        /// bad night.
        /// </summary>
        public int FailureCount { get; set; }
    }
}
