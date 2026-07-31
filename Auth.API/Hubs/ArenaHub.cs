using System.Security.Claims;
using Auth.Models.Entities.Games;
using Auth.Services.Interfaces.Games;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Auth.API.Hubs
{
    /// <summary>
    /// The transport for Comet Arena. Deliberately thin.
    ///
    /// Every method here does one of two things: take an input and hand it to the
    /// simulation, or read state. There is no game logic in this file at all, and there is
    /// no method a client can call that sets a score — which is the whole reason the
    /// leaderboard can be trusted. The score is not something the client is allowed to
    /// mention.
    ///
    /// Group membership is by session id, so a snapshot goes to the four people in a match
    /// rather than to everyone connected.
    /// </summary>
    [Authorize]
    public class ArenaHub : Hub
    {
        private readonly IArenaService _arena;

        public ArenaHub(IArenaService arena)
        {
            _arena = arena;
        }

        private string UserId => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        private string DisplayName
        {
            get
            {
                var name = $"{Context.User?.FindFirstValue("FirstName")} {Context.User?.FindFirstValue("LastName")}".Trim();
                return string.IsNullOrWhiteSpace(name) ? "Scholar" : name;
            }
        }

        public async Task<string> CreateMatch(int mode)
        {
            var sessionId = _arena.CreateSession((ArenaMode)mode, UserId, DisplayName);
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            return sessionId;
        }

        public async Task<bool> JoinMatch(string sessionId)
        {
            if (!_arena.Join(sessionId, UserId, DisplayName)) return false;

            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
            return true;
        }

        public async Task LeaveMatch(string sessionId)
        {
            _arena.Leave(sessionId, UserId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        }

        public bool StartMatch(string sessionId) => _arena.Start(sessionId, UserId);

        /// <summary>
        /// The only thing a client sends during play: a direction and whether it dashed.
        ///
        /// Called up to 30 times a second per player, so it deliberately returns void — a
        /// Task round trip per input would add the client's latency to every frame of
        /// movement.
        /// </summary>
        public void SendInput(string sessionId, float x, float y, bool dash) =>
            _arena.SetInput(sessionId, UserId, x, y, dash);

        public IReadOnlyList<ArenaLobbyDto> GetLobbies() => _arena.GetOpenLobbies();
    }
}
