using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Auth.API.Hubs
{
    /// <summary>
    /// Live delivery of notifications to whoever has the app open.
    ///
    /// Kept separate from <see cref="MinigamesHub"/> rather than folded into it. That hub
    /// holds a lot of per-connection game state in static dictionaries and broadcasts on a
    /// 150ms throttle during a duel; putting the bell menu on the same connection would mean
    /// a scholar who never plays a minigame still pays for that machinery, and a bug in
    /// duel-state cleanup could take notifications down with it.
    ///
    /// The hub itself has no methods worth calling. Delivery is entirely server-to-client,
    /// addressed with <c>Clients.User(...)</c>, which SignalR resolves through the default
    /// user-id provider — the same <c>ClaimTypes.NameIdentifier</c> every controller reads.
    /// That means a scholar with the app open on a laptop and a phone gets both updated,
    /// with no group bookkeeping to leak on a dropped connection.
    /// </summary>
    [Authorize]
    public class NotificationsHub : Hub
    {
        /// <summary>
        /// Round-trip used by the client to confirm the connection is live before it stops
        /// polling. Without it a client cannot distinguish "connected and quiet" from
        /// "silently broken", and would either poll forever or miss everything.
        /// </summary>
        public Task<string> Ping() => Task.FromResult("pong");
    }
}
