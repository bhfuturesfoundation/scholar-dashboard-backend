namespace Auth.Services.Interfaces.Notifications
{
    /// <summary>
    /// Who is currently connected.
    ///
    /// Deliberately derived from live hub connections rather than from a "last seen"
    /// column. A timestamp needs a heartbeat to stay honest and still shows people as
    /// present for minutes after they close the tab; a connection is present exactly as
    /// long as the socket is.
    ///
    /// KNOWN LIMIT: the state is in memory, so it is per-instance. On a single instance —
    /// which is what this deployment runs — it is exact. Behind more than one, each would
    /// report only its own connections. Making it global needs the Redis backplane the app
    /// already optionally configures, which is a contained change to this one class.
    /// </summary>
    public interface IPresenceTracker
    {
        /// <summary>
        /// Records a connection. Returns true when this is the user's *first* one, which is
        /// the only case worth broadcasting — opening a second tab is not an arrival.
        /// </summary>
        bool Connect(string userId, string connectionId, PresenceUser user);

        /// <summary>
        /// Removes a connection. Returns true when it was the user's last, i.e. they have
        /// actually gone rather than closed one of several tabs.
        /// </summary>
        bool Disconnect(string userId, string connectionId);

        IReadOnlyList<PresenceUser> GetOnline();

        int OnlineCount { get; }

        bool IsOnline(string userId);
    }

    /// <summary>
    /// The subset of a person shown in the presence list.
    ///
    /// Email is deliberately absent. The list is visible to every signed-in account, and a
    /// roster of names is a very different thing from a harvestable list of addresses.
    /// </summary>
    public record PresenceUser(
        string UserId,
        string DisplayName,
        string? Title,
        string Role,
        DateTime ConnectedAtUtc);
}
