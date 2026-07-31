using Auth.Models.DTOs.Notifications;

namespace Auth.Services.Interfaces.Notifications
{
    /// <summary>
    /// Pushes a notification down an open connection so the bell menu updates without a
    /// refresh.
    ///
    /// Defined here rather than taking <c>IHubContext</c> directly because Auth.Services is
    /// a plain class library and SignalR lives in the API project. The API registers the
    /// implementation; everything else depends on this interface and stays testable without
    /// a hub.
    ///
    /// Implementations must never throw. A dropped realtime frame is cosmetic — the
    /// notification is already in the database and the client will pick it up on its next
    /// poll or page load — so a failure here must not roll back the thing that caused it.
    /// </summary>
    public interface INotificationRealtime
    {
        Task NotifyAsync(string userId, NotificationDto notification, CancellationToken cancellationToken = default);

        /// <summary>Tells one user's open tabs that their unread count changed.</summary>
        Task NotifyUnreadCountAsync(string userId, int unreadCount, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Used when SignalR is not wired up — background services resolve the interface and
    /// should not have to null-check it.
    /// </summary>
    public class NullNotificationRealtime : INotificationRealtime
    {
        public Task NotifyAsync(string userId, NotificationDto notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task NotifyUnreadCountAsync(string userId, int unreadCount, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
