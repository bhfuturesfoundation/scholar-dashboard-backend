using Auth.API.Hubs;
using Auth.Models.DTOs.Notifications;
using Auth.Services.Interfaces.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Auth.API.Services
{
    /// <summary>
    /// Bridges <see cref="INotificationRealtime"/> onto SignalR.
    ///
    /// Lives in the API project because that is where SignalR is configured; Auth.Services
    /// is a plain class library and depends only on the interface, which keeps
    /// <c>NotificationService</c> unit-testable without a hub or a host.
    ///
    /// Nothing here throws. A realtime frame is a nicety — the notification is already
    /// committed and the client will see it on its next load — so a transport failure must
    /// never propagate back into the transaction that caused it.
    /// </summary>
    public class SignalRNotificationRealtime : INotificationRealtime
    {
        private readonly IHubContext<NotificationsHub> _hub;
        private readonly ILogger<SignalRNotificationRealtime> _logger;

        public SignalRNotificationRealtime(
            IHubContext<NotificationsHub> hub,
            ILogger<SignalRNotificationRealtime> logger)
        {
            _hub = hub;
            _logger = logger;
        }

        public async Task NotifyAsync(
            string userId, NotificationDto notification, CancellationToken cancellationToken = default)
        {
            try
            {
                await _hub.Clients.User(userId).SendAsync("NotificationReceived", notification, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not push notification {Id} to {User}.", notification.Id, userId);
            }
        }

        public async Task NotifyUnreadCountAsync(
            string userId, int unreadCount, CancellationToken cancellationToken = default)
        {
            try
            {
                await _hub.Clients.User(userId).SendAsync("UnreadCountChanged", unreadCount, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not push unread count to {User}.", userId);
            }
        }
    }
}
