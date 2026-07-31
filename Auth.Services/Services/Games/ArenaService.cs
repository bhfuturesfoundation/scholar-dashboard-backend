using System.Collections.Concurrent;
using System.Security.Cryptography;
using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Entities.Games;
using Auth.Services.Interfaces.Games;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Games
{
    /// <inheritdoc cref="IArenaService"/>
    public class ArenaService : IArenaService
    {
        public const string GameId = "comet-arena";

        private const int Capacity = 4;

        /// <summary>Snapshots go out every other tick — 15 Hz. See TickAllAsync.</summary>
        private const int SnapshotEveryNTicks = 2;

        /// <summary>A lobby nobody starts is cleaned up after this long.</summary>
        private static readonly TimeSpan LobbyTimeout = TimeSpan.FromMinutes(10);

        private sealed class Session
        {
            public required ArenaState State { get; init; }
            public required string HostUserId { get; init; }
            public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
            public bool Recorded { get; set; }
        }

        private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IArenaRealtime _realtime;
        private readonly ILogger<ArenaService> _logger;

        public ArenaService(
            IServiceScopeFactory scopeFactory,
            IArenaRealtime realtime,
            ILogger<ArenaService> logger)
        {
            _scopeFactory = scopeFactory;
            _realtime = realtime;
            _logger = logger;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        public string CreateSession(ArenaMode mode, string hostUserId, string hostDisplayName)
        {
            var sessionId = Guid.NewGuid().ToString("N")[..10];

            // Cryptographic seed rather than a tick count, so nobody can predict where orbs
            // will spawn by knowing when they pressed start.
            var seed = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

            var state = ArenaSimulation.CreateSession(sessionId, mode, seed);
            ArenaSimulation.AddPlayer(state, hostUserId, hostDisplayName);

            // Solo has nobody to wait for, so it skips the lobby entirely.
            if (mode == ArenaMode.Solo)
            {
                state.Phase = ArenaPhase.Countdown;
                state.Tick = 0;
            }

            _sessions[sessionId] = new Session { State = state, HostUserId = hostUserId };

            _logger.LogInformation("Arena session {Session} created ({Mode}) by {User}.", sessionId, mode, hostUserId);
            return sessionId;
        }

        public bool Join(string sessionId, string userId, string displayName)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return false;

            var state = session.State;

            lock (state)
            {
                // Rejoining your own match mid-play is allowed — a dropped phone connection
                // should not forfeit a run that is already scoring.
                if (state.Players.Any(p => p.UserId == userId))
                {
                    var existing = state.Players.First(p => p.UserId == userId);
                    existing.Connected = true;
                    return true;
                }

                if (state.Phase != ArenaPhase.Lobby) return false;
                if (state.Players.Count >= Capacity) return false;

                ArenaSimulation.AddPlayer(state, userId, displayName);
            }

            session.LastActivityUtc = DateTime.UtcNow;
            return true;
        }

        public void Leave(string sessionId, string userId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return;

            var state = session.State;

            lock (state)
            {
                var player = state.Players.FirstOrDefault(p => p.UserId == userId);
                if (player is null) return;

                // Marked disconnected rather than removed. Their score still counts and is
                // still recorded — leaving a match you were losing should not erase the
                // result, and leaving one you were winning should not erase it either.
                player.Connected = false;
                player.InputX = 0;
                player.InputY = 0;

                // Nobody left watching: end it now rather than simulating an empty arena
                // for the remaining eighty seconds.
                if (state.Players.All(p => !p.Connected) && state.Phase != ArenaPhase.Finished)
                {
                    state.Phase = ArenaPhase.Finished;
                    state.FinishedAtUtc = DateTime.UtcNow;
                }
            }
        }

        public bool Start(string sessionId, string userId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return false;
            if (session.HostUserId != userId) return false;

            lock (session.State)
            {
                if (session.State.Phase != ArenaPhase.Lobby) return false;

                session.State.Phase = ArenaPhase.Countdown;
                session.State.Tick = 0;
            }

            return true;
        }

        public void SetInput(string sessionId, string userId, float x, float y, bool dash)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return;

            lock (session.State)
            {
                ArenaSimulation.SetInput(session.State, userId, x, y, dash);
            }

            session.LastActivityUtc = DateTime.UtcNow;
        }

        public ArenaState? GetState(string sessionId) =>
            _sessions.TryGetValue(sessionId, out var session) ? session.State : null;

        public IReadOnlyList<ArenaLobbyDto> GetOpenLobbies() =>
            _sessions.Values
                .Where(s => s.State.Phase == ArenaPhase.Lobby && s.State.Mode != ArenaMode.Solo)
                .Select(s => new ArenaLobbyDto
                {
                    SessionId = s.State.SessionId,
                    Mode = s.State.Mode,
                    HostName = s.State.Players.FirstOrDefault(p => p.UserId == s.HostUserId)?.DisplayName ?? "—",
                    PlayerCount = s.State.Players.Count(p => p.Connected),
                    Capacity = Capacity,
                })
                .ToList();

        // ── The loop ──────────────────────────────────────────────────────────

        public async Task TickAllAsync(CancellationToken cancellationToken)
        {
            foreach (var (sessionId, session) in _sessions)
            {
                if (cancellationToken.IsCancellationRequested) return;

                var state = session.State;
                bool finished;
                object? snapshot = null;

                lock (state)
                {
                    if (state.Phase is ArenaPhase.Lobby)
                    {
                        // A lobby nobody ever starts would otherwise sit in memory forever.
                        if (DateTime.UtcNow - session.LastActivityUtc > LobbyTimeout)
                        {
                            _sessions.TryRemove(sessionId, out _);
                        }
                        continue;
                    }

                    if (state.Phase != ArenaPhase.Finished)
                    {
                        ArenaSimulation.Tick(state);

                        // Snapshots at half the tick rate. The simulation needs 30 Hz to feel
                        // right; the wire does not, because the client interpolates between
                        // snapshots anyway. Halving this halves bandwidth for free.
                        if (state.Tick % SnapshotEveryNTicks == 0) snapshot = BuildSnapshot(state);
                    }

                    finished = state.Phase == ArenaPhase.Finished;
                }

                if (snapshot is not null)
                {
                    await _realtime.SendSnapshotAsync(sessionId, snapshot, cancellationToken);
                }

                if (finished && !session.Recorded)
                {
                    session.Recorded = true;
                    await RecordResultAsync(session, cancellationToken);
                    _sessions.TryRemove(sessionId, out _);
                }
            }
        }

        /// <summary>
        /// The wire format.
        ///
        /// Deliberately terse field names and rounded integers rather than the full state
        /// objects: this goes out fifteen times a second per player, and floats serialised
        /// to seventeen significant figures triple the payload for precision no one can see
        /// at one pixel.
        /// </summary>
        private static object BuildSnapshot(ArenaState state) => new
        {
            t = state.Tick,
            ph = (int)state.Phase,
            p = state.Players.Select(p => new
            {
                id = p.UserId,
                n = p.DisplayName,
                c = p.ColorIndex,
                x = (int)p.X,
                y = (int)p.Y,
                s = p.Score,

                // The pouch. The client draws this separately from the banked score, so a
                // player can see what is at stake without doing arithmetic.
                ca = p.Carried,
                bk = p.BankingTicks,
                nm = p.NearMissFlashTicks,

                cb = p.Combo,
                m = ArenaSimulation.Multiplier(p.Combo),
                st = p.StunTicks,
                dc = p.DashCooldown,
                on = p.Connected,
            }),
            o = state.Orbs.Select(o => new { i = o.Id, x = (int)o.X, y = (int)o.Y, v = o.Value }),
            c = state.Comets.Select(c => new
            {
                i = c.Id,
                x = (int)c.X,
                y = (int)c.Y,
                r = (int)c.Radius,

                // Above zero this is a telegraph, not a comet: the client draws a warning
                // line along (dx, dy) instead of a solid body.
                w = c.WarningTicks,
                dx = (int)(c.DirectionX * 100),
                dy = (int)(c.DirectionY * 100),
            }),
        };

        /// <summary>
        /// Persists the result.
        ///
        /// This is the only place a score is ever written for this game, and it writes a
        /// number the server computed itself — which is the entire point of the exercise.
        /// Marked Verified so the leaderboard can separate these from the old
        /// client-submitted rows without deleting the history.
        /// </summary>
        private async Task RecordResultAsync(Session session, CancellationToken cancellationToken)
        {
            var state = session.State;

            // Nothing to record for a match that ended before it started.
            if (state.Tick < ArenaSimulation.TicksPerSecond * 5) return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var durationSeconds = state.Tick / ArenaSimulation.TicksPerSecond;

                foreach (var player in state.Players)
                {
                    context.GameScores.Add(new GameScore
                    {
                        UserId = player.UserId,
                        GameId = GameId,
                        Score = player.Score,
                        PlayedAt = DateTime.UtcNow,
                        Verified = true,
                        SessionId = state.SessionId,
                        Mode = (int)state.Mode,
                        DurationSeconds = durationSeconds,
                        BestCombo = player.BestCombo,
                    });
                }

                await context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Arena session {Session} finished; recorded {Count} verified score(s).",
                    state.SessionId, state.Players.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not record arena result for {Session}.", state.SessionId);
            }

            await _realtime.SendFinishedAsync(state.SessionId, new
            {
                sessionId = state.SessionId,
                mode = (int)state.Mode,
                players = state.Players
                    .OrderByDescending(p => p.Score)
                    .Select(p => new
                    {
                        userId = p.UserId,
                        name = p.DisplayName,
                        score = p.Score,
                        bestCombo = p.BestCombo,
                        orbs = p.OrbsCollected,
                        hits = p.CometHits,
                        nearMisses = p.NearMisses,

                        // What they were still holding when time ran out. Never added to the
                        // score — the last thing the game should teach is that hoarding to
                        // the buzzer works.
                        lost = p.Carried,
                        mostCarried = p.MostCarried,
                    }),
            }, cancellationToken);
        }
    }
}
