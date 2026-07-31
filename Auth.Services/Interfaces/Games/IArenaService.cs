using Auth.Models.Entities.Games;

namespace Auth.Services.Interfaces.Games
{
    /// <summary>
    /// Owns every live Comet Arena match.
    ///
    /// A singleton holding in-memory state, for the same reason presence is: a match exists
    /// for ninety seconds and is meaningless once the process restarts. Persisting the world
    /// each tick would be thirty database writes a second to store something nobody will
    /// ever read. Only the *result* is persisted.
    /// </summary>
    public interface IArenaService
    {
        /// <summary>Creates a match and returns its id. Solo matches are private to the caller.</summary>
        string CreateSession(ArenaMode mode, string hostUserId, string hostDisplayName);

        /// <summary>Adds a player. Returns false when the match is full or already running.</summary>
        bool Join(string sessionId, string userId, string displayName);

        void Leave(string sessionId, string userId);

        /// <summary>Host-only: moves a lobby into the countdown.</summary>
        bool Start(string sessionId, string userId);

        void SetInput(string sessionId, string userId, float x, float y, bool dash);

        ArenaState? GetState(string sessionId);

        /// <summary>Matches waiting for players, for the "join a game" list.</summary>
        IReadOnlyList<ArenaLobbyDto> GetOpenLobbies();

        /// <summary>Advances every running match by one tick. Called by the host loop only.</summary>
        Task TickAllAsync(CancellationToken cancellationToken);
    }

    public sealed class ArenaLobbyDto
    {
        public string SessionId { get; set; } = string.Empty;
        public ArenaMode Mode { get; set; }
        public string HostName { get; set; } = string.Empty;
        public int PlayerCount { get; set; }
        public int Capacity { get; set; }
    }

    /// <summary>
    /// Pushes match state to the players in it.
    ///
    /// Defined here rather than taking IHubContext directly, because Auth.Services is a
    /// plain class library and SignalR lives in the API project — the same split the
    /// notification hub uses.
    /// </summary>
    public interface IArenaRealtime
    {
        Task SendSnapshotAsync(string sessionId, object snapshot, CancellationToken cancellationToken = default);

        Task SendFinishedAsync(string sessionId, object result, CancellationToken cancellationToken = default);
    }
}
