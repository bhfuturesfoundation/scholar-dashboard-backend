using System.Security.Claims;
using Auth.Models.Constants;
using Auth.Services.Interfaces.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Auth.API.Hubs
{
    /// <summary>
    /// Live delivery of notifications, and the source of truth for who is online.
    ///
    /// Kept separate from <see cref="MinigamesHub"/> rather than folded into it. That hub
    /// holds a lot of per-connection game state in static dictionaries and broadcasts on a
    /// 150 ms throttle during a duel; putting the bell menu and presence on the same
    /// connection would mean a scholar who never plays a minigame still pays for that
    /// machinery, and a bug in duel-state cleanup could take both down with it.
    ///
    /// Presence is derived from the connection lifecycle here rather than from a "last
    /// seen" column, because a timestamp needs a heartbeat to stay honest and still shows
    /// people as present for minutes after they close the tab.
    /// </summary>
    [Authorize]
    public class NotificationsHub : Hub
    {
        private readonly IPresenceTracker _presence;
        private readonly ILogger<NotificationsHub> _logger;

        public NotificationsHub(IPresenceTracker presence, ILogger<NotificationsHub> logger)
        {
            _presence = presence;
            _logger = logger;
        }

        private string UserId => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        public override async Task OnConnectedAsync()
        {
            var user = BuildPresenceUser();

            if (user is not null && _presence.Connect(UserId, Context.ConnectionId, user))
            {
                // Only on the *first* connection. Opening a second tab is not an arrival,
                // and broadcasting it would make the roster flicker.
                await Clients.Others.SendAsync("PresenceJoined", user);
            }

            // The joiner always gets the current roster, even on a second tab, because that
            // tab has no state of its own yet.
            await Clients.Caller.SendAsync("PresenceSnapshot", _presence.GetOnline());

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (_presence.Disconnect(UserId, Context.ConnectionId))
            {
                await Clients.Others.SendAsync("PresenceLeft", UserId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>Lets a client re-sync without reconnecting, e.g. after a network blip.</summary>
        public Task<IReadOnlyList<PresenceUser>> GetPresence() => Task.FromResult(_presence.GetOnline());

        /// <summary>
        /// Round-trip used by the client to confirm the connection is live before it stops
        /// polling. Without it a client cannot distinguish "connected and quiet" from
        /// "silently broken", and would either poll forever or miss everything.
        /// </summary>
        public Task<string> Ping() => Task.FromResult("pong");

        /// <summary>
        /// Builds the roster entry from claims rather than a database read.
        ///
        /// OnConnectedAsync runs for every socket, including reconnects on a flaky mobile
        /// connection, so a query here would be a query per reconnect per user. Everything
        /// shown in the list is already in the token.
        /// </summary>
        private PresenceUser? BuildPresenceUser()
        {
            if (string.IsNullOrEmpty(UserId)) return null;

            var first = Context.User?.FindFirstValue("FirstName");
            var last = Context.User?.FindFirstValue("LastName");
            var name = $"{first} {last}".Trim();

            if (string.IsNullOrWhiteSpace(name)) name = "A scholar";

            // The most specific role wins, so a program manager is not listed as "User"
            // just because the claim happened to come back in that order.
            var role =
                Context.User?.IsInRole(AppRoles.Admin) == true ? AppRoles.Admin
                : Context.User?.IsInRole(AppRoles.ProgramManager) == true ? AppRoles.ProgramManager
                : Context.User?.IsInRole(AppRoles.Mentor) == true ? AppRoles.Mentor
                : AppRoles.User;

            return new PresenceUser(UserId, name, null, role, DateTime.UtcNow);
        }
    }
}
