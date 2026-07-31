namespace Auth.Services.Interfaces.Notifications
{
    /// <summary>
    /// Delivers a web push message to one subscribed device.
    ///
    /// Push is the only channel that reaches somebody whose laptop is shut and who has not
    /// opened their email — which, for a monthly deadline, is most people most of the time.
    /// It is also the channel people punish you for, which is why every push category
    /// except journal deadlines and duel invites defaults to off.
    /// </summary>
    public interface IPushSender
    {
        /// <summary>False when VAPID keys are absent, in which case nothing is attempted.</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// The VAPID public key the browser needs to create a subscription. Null when
        /// unconfigured — the settings screen uses this to hide the push column rather than
        /// offering a switch that cannot work.
        /// </summary>
        string? PublicKey { get; }

        /// <summary>Human-readable reason push is unavailable, for the operations console.</summary>
        string ConfigurationHint { get; }

        /// <summary>
        /// Sends to one endpoint. Never throws — the result says what happened, because a
        /// dead phone must not abort a broadcast to two hundred others.
        /// </summary>
        Task<PushSendResult> SendAsync(
            PushTarget target, PushPayload payload, CancellationToken cancellationToken = default);
    }

    /// <summary>The three pieces a push service needs to reach one device.</summary>
    public record PushTarget(string Endpoint, string P256dh, string Auth);

    /// <summary>What the service worker receives and renders as a system notification.</summary>
    public class PushPayload
    {
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;

        /// <summary>Relative path opened when the notification is tapped.</summary>
        public string? Url { get; set; }

        /// <summary>
        /// Groups notifications on the device. Two pushes with the same tag replace each
        /// other rather than stacking, so a reminder that fires twice shows once.
        /// </summary>
        public string? Tag { get; set; }

        /// <summary>Echoed back by the service worker so a click can be attributed.</summary>
        public int? NotificationId { get; set; }
    }

    public class PushSendResult
    {
        public bool Success { get; set; }

        /// <summary>
        /// True when the push service says this subscription is permanently gone (404/410).
        /// The caller deletes the row: the browser it belonged to has been cleared or
        /// uninstalled, and retrying forever would be a slow leak of dead endpoints.
        /// </summary>
        public bool SubscriptionExpired { get; set; }

        public string? Error { get; set; }

        public static PushSendResult Ok() => new() { Success = true };
        public static PushSendResult Expired(string error) => new() { SubscriptionExpired = true, Error = error };
        public static PushSendResult Failed(string error) => new() { Error = error };
    }
}
