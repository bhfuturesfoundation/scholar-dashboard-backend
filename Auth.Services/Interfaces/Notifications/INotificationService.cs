using Auth.Models.DTOs.Notifications;
using Auth.Models.Entities.Notifications;

namespace Auth.Services.Interfaces.Notifications
{
    /// <summary>
    /// Creating and reading notifications.
    ///
    /// Creation only writes the row and pushes it down the realtime connection. Email and
    /// push are NOT sent here — the row itself is the queue, and
    /// <c>NotificationSchedulerService</c> drains it. This is the transactional outbox
    /// pattern, and it buys three things that matter:
    ///
    /// * awarding a badge no longer blocks on SMTP, so a slow mail provider cannot make the
    ///   progress page time out;
    /// * a notification created while the mail provider is down still goes out when it
    ///   recovers, instead of being lost in a fire-and-forget task;
    /// * quiet hours are just a column, not a scheduled callback that a deploy would drop.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Creates one notification, or returns null when it was suppressed — because an
        /// identical <c>DedupeKey</c> already exists, or because it collapsed into a recent
        /// unread notification with the same <c>CollapseKey</c>.
        /// </summary>
        Task<Notification?> CreateAsync(
            CreateNotificationRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates many at once, for broadcasts. Returns how many rows were actually written
        /// after dedupe and collapsing.
        /// </summary>
        Task<int> CreateManyAsync(
            IReadOnlyCollection<CreateNotificationRequest> requests,
            CancellationToken cancellationToken = default);

        /// <summary>The caller's undismissed notifications, newest first.</summary>
        Task<NotificationListDto> GetForUserAsync(
            string userId, int limit = 50, CancellationToken cancellationToken = default);

        Task<int> MarkReadAsync(
            string userId, IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

        Task<int> MarkAllReadAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>Hides one notification. Does not delete it.</summary>
        Task<bool> DismissAsync(string userId, int id, CancellationToken cancellationToken = default);

        /// <summary>Hides every notification currently visible to this user.</summary>
        Task<int> DismissAllAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// This user's preferences, creating a default row on first read so the settings
        /// screen and the send path always agree on what the defaults are.
        /// </summary>
        Task<NotificationPreference> GetPreferenceAsync(
            string userId, CancellationToken cancellationToken = default);

        Task<NotificationPreference> UpdatePreferenceAsync(
            string userId, NotificationPreferenceDto update, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One notification to create. A record rather than the entity so callers cannot set
    /// delivery timestamps or ids by accident.
    /// </summary>
    public class CreateNotificationRequest
    {
        public string UserId { get; set; } = string.Empty;

        /// <summary>A key from <c>NotificationKeys</c>.</summary>
        public string MessageKey { get; set; } = string.Empty;

        /// <summary>Substitution values. Rendered client-side for in-app, server-side for email.</summary>
        public Dictionary<string, string> Params { get; set; } = new();

        /// <summary>Overrides the key's default action. Usually left null.</summary>
        public string? ActionUrl { get; set; }

        /// <summary>Send at most one notification per user per key value.</summary>
        public string? DedupeKey { get; set; }

        /// <summary>Merge into a recent unread notification sharing this key.</summary>
        public string? CollapseKey { get; set; }

        /// <summary>
        /// Ask for an email. Still subject to the recipient's preferences and to the
        /// existing suppression list — this is a request, not an instruction.
        /// </summary>
        public bool WantsEmail { get; set; }

        public bool WantsPush { get; set; }

        public int? AnnouncementId { get; set; }
    }
}
