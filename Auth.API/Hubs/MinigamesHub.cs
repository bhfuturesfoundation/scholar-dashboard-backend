using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Auth.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;

namespace Auth.API.Hubs
{
    [Authorize]
    public class MinigamesHub : Hub
    {
        private static readonly ConcurrentDictionary<string, RoomState> Rooms = new();
        private static readonly ConcurrentDictionary<string, DuelInvite> Invites = new();
        private static readonly ConcurrentDictionary<string, DuelSession> DuelSessions = new();
        private static readonly ConcurrentDictionary<string, ChessMatchState> ChessStates = new();
        private static readonly ConcurrentDictionary<string, ConnectFourState> ConnectFourStates = new();
        private static readonly ConcurrentDictionary<string, ShufflePoolEntry> ShufflePool = new();
        private static readonly object ShuffleLock = new();

        private static readonly TimeSpan RoomTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan InviteTtl = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan DuelTtl = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan ShuffleTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan BroadcastThrottle = TimeSpan.FromMilliseconds(150);

        private const int DefaultRoundSeconds = 10;
        private const int SignalSmashRoundSeconds = 20;
        private const int NeonRunnerRoundSeconds = 45;
        private const int CardClashRoundSeconds = 60;
        private const int KnightTacticsRoundSeconds = 75;
        private const int ChessArenaRoundSeconds = 1800;
        private const int ConnectFourRoundSeconds = 900;

        private readonly IHubContext<MinigamesHub> _hubContext;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<MinigamesHub> _logger;

        public MinigamesHub(
            IHubContext<MinigamesHub> hubContext,
            UserManager<User> userManager,
            ILogger<MinigamesHub> logger)
        {
            _hubContext = hubContext;
            _userManager = userManager;
            _logger = logger;
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.TryGetValue("room", out var roomValue) && roomValue is string roomCode)
            {
                await LeaveRoom(roomCode);
            }

            if (Context.Items.TryGetValue("duel", out var duelValue) && duelValue is string duelCode)
            {
                await LeaveDuelSession(duelCode);
            }

            var disconnectedUserId = GetCurrentUserId();
            if (!string.IsNullOrWhiteSpace(disconnectedUserId))
            {
                await RemoveFromShufflePoolAsync(disconnectedUserId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Legacy room multiplayer retained for backward compatibility.
        public async Task JoinRoom(string roomCode, string displayName)
        {
            var normalized = NormalizeCode(roomCode);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            if (Context.Items.TryGetValue("room", out var roomValue) && roomValue is string existingRoom)
            {
                if (!string.Equals(existingRoom, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    await LeaveRoom(existingRoom);
                }
            }

            var room = Rooms.GetOrAdd(normalized, _ => new RoomState(DefaultRoundSeconds));
            room.Touch();

            room.Players[Context.ConnectionId] = new PlayerState
            {
                ConnectionId = Context.ConnectionId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Scholar" : displayName.Trim(),
                Score = 0,
            };

            Context.Items["room"] = normalized;

            await Groups.AddToGroupAsync(Context.ConnectionId, normalized);
            await BroadcastRoomAsync(normalized, room, force: true);
        }

        public async Task LeaveRoom(string roomCode)
        {
            var normalized = NormalizeCode(roomCode);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            if (Rooms.TryGetValue(normalized, out var room))
            {
                room.Players.TryRemove(Context.ConnectionId, out _);
                room.Touch();

                await Groups.RemoveFromGroupAsync(Context.ConnectionId, normalized);

                if (room.Players.IsEmpty)
                {
                    Rooms.TryRemove(normalized, out _);
                }
                else
                {
                    await BroadcastRoomAsync(normalized, room, force: true);
                }
            }

            Context.Items.Remove("room");
        }

        public async Task StartRound(string roomCode)
        {
            var normalized = NormalizeCode(roomCode);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            if (!Rooms.TryGetValue(normalized, out var room)) return;

            lock (room.SyncRoot)
            {
                if (room.IsRunning) return;

                room.IsRunning = true;
                room.RoundToken++;
                room.RoundEndsAt = DateTimeOffset.UtcNow.AddSeconds(room.RoundSeconds);

                foreach (var player in room.Players.Values)
                {
                    player.Score = 0;
                }

                room.Touch();
            }

            await BroadcastRoomAsync(normalized, room, force: true);

            _ = Task.Run(async () =>
            {
                var token = room.RoundToken;
                await Task.Delay(TimeSpan.FromSeconds(room.RoundSeconds));

                var shouldBroadcast = false;
                lock (room.SyncRoot)
                {
                    if (room.IsRunning && room.RoundToken == token)
                    {
                        room.IsRunning = false;
                        room.RoundEndsAt = null;
                        room.Touch();
                        shouldBroadcast = true;
                    }
                }

                if (shouldBroadcast)
                {
                    await BroadcastRoomAsync(normalized, room, force: true);
                }
            });
        }

        public async Task Tap(string roomCode)
        {
            var normalized = NormalizeCode(roomCode);
            if (string.IsNullOrWhiteSpace(normalized)) return;
            if (!Rooms.TryGetValue(normalized, out var room)) return;
            if (!room.IsRunning) return;

            if (room.Players.TryGetValue(Context.ConnectionId, out var player))
            {
                player.Score++;
                room.Touch();
                await BroadcastRoomAsync(normalized, room, force: false);
            }
        }


        // New invite-based multiplayer flow (no manual room sharing).
        public async Task SendDuelInviteByEmail(string targetEmail, string senderDisplayName, string gameId = "signal-smash-duel")
        {
            CleanupState();

            var senderUserId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(senderUserId))
            {
                throw new HubException("Authentication required.");
            }

            if (string.IsNullOrWhiteSpace(targetEmail))
            {
                throw new HubException("Target email is required.");
            }

            var normalizedEmail = targetEmail.Trim();
            var targetUser = await _userManager.FindByEmailAsync(normalizedEmail);
            if (targetUser == null)
            {
                throw new HubException("Target user not found.");
            }

            await SendDuelInviteToUserInternal(targetUser, senderUserId, senderDisplayName, gameId);
        }

        public async Task SendDuelInviteToUser(string targetUserId, string senderDisplayName, string gameId = "signal-smash-duel")
        {
            CleanupState();

            var senderUserId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(senderUserId))
            {
                throw new HubException("Authentication required.");
            }

            if (string.IsNullOrWhiteSpace(targetUserId))
            {
                throw new HubException("Target user is required.");
            }

            var targetUser = await _userManager.FindByIdAsync(targetUserId.Trim());
            if (targetUser == null)
            {
                throw new HubException("Target user not found.");
            }

            await SendDuelInviteToUserInternal(targetUser, senderUserId, senderDisplayName, gameId);
        }

        private async Task SendDuelInviteToUserInternal(User targetUser, string senderUserId, string senderDisplayName, string gameId)
        {
            if (targetUser.Id == senderUserId)
            {
                throw new HubException("You cannot invite yourself.");
            }

            var safeGameId = NormalizeGameId(gameId);
            var inviteId = Guid.NewGuid().ToString("N");
            var fromDisplay = string.IsNullOrWhiteSpace(senderDisplayName) ? "Scholar" : senderDisplayName.Trim();
            var targetDisplay = BuildDisplayName(targetUser);
            var invite = new DuelInvite(inviteId, senderUserId, targetUser.Id, fromDisplay, DateTimeOffset.UtcNow, safeGameId);

            Invites[inviteId] = invite;

            _logger.LogInformation("Minigame invite {InviteId} created from {Sender} to {Target} for {GameId}", inviteId, senderUserId, targetUser.Id, safeGameId);

            await _hubContext.Clients.User(targetUser.Id).SendAsync(
                "MinigameInviteReceived",
                new DuelInvitePayload(inviteId, invite.FromUserId, invite.FromDisplayName, invite.GameId, invite.CreatedAt.Add(InviteTtl))
            );

            await _hubContext.Clients.User(senderUserId).SendAsync(
                "MinigameInviteStatus",
                new InviteStatusPayload(inviteId, "pending", null, targetDisplay, invite.GameId)
            );
        }

        private static string BuildDisplayName(User user)
        {
            var first = user.FirstName ?? string.Empty;
            var last = user.LastName ?? string.Empty;
            var full = $"{first} {last}".Trim();
            if (!string.IsNullOrWhiteSpace(full))
            {
                return full;
            }

            return string.IsNullOrWhiteSpace(user.Email) ? "Scholar" : user.Email;
        }

        public async Task<InviteResponsePayload> RespondToDuelInvite(string inviteId, bool accept, string receiverDisplayName)
        {
            CleanupState();

            var receiverUserId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(receiverUserId))
            {
                throw new HubException("Authentication required.");
            }

            if (string.IsNullOrWhiteSpace(inviteId))
            {
                throw new HubException("Invite id is required.");
            }

            if (!Invites.TryRemove(inviteId, out var invite))
            {
                throw new HubException("Invite no longer exists.");
            }

            if (!string.Equals(invite.ToUserId, receiverUserId, StringComparison.OrdinalIgnoreCase))
            {
                throw new HubException("Invite does not belong to current user.");
            }

            if (invite.CreatedAt.Add(InviteTtl) <= DateTimeOffset.UtcNow)
            {
                var expiredPayload = new InviteStatusPayload(invite.InviteId, "expired", null, invite.FromDisplayName, invite.GameId);
                await _hubContext.Clients.User(invite.FromUserId).SendAsync("MinigameInviteStatus", expiredPayload);
                await _hubContext.Clients.User(invite.ToUserId).SendAsync("MinigameInviteStatus", expiredPayload);
                return new InviteResponsePayload(invite.InviteId, "expired", null, invite.FromDisplayName, invite.GameId);
            }

            if (!accept)
            {
                var declinedPayload = new InviteStatusPayload(invite.InviteId, "declined", null, invite.FromDisplayName, invite.GameId);
                await _hubContext.Clients.User(invite.FromUserId).SendAsync("MinigameInviteStatus", declinedPayload);
                await _hubContext.Clients.User(invite.ToUserId).SendAsync("MinigameInviteStatus", declinedPayload);
                return new InviteResponsePayload(invite.InviteId, "declined", null, invite.FromDisplayName, invite.GameId);
            }

            var roundSeconds = invite.GameId switch
            {
                "neon-runner-duel" => NeonRunnerRoundSeconds,
                "card-clash-duel" => CardClashRoundSeconds,
                "knight-tactics-duel" => KnightTacticsRoundSeconds,
                "chess-arena-duel" => ChessArenaRoundSeconds,
                "connect-four-arena-duel" => ConnectFourRoundSeconds,
                _ => SignalSmashRoundSeconds,
            };
            var sessionId = GenerateSessionId();
            var duel = new DuelSession(sessionId, roundSeconds, invite.GameId);

            var receiverDisplay = string.IsNullOrWhiteSpace(receiverDisplayName) ? "Scholar" : receiverDisplayName.Trim();
            duel.Players[invite.FromUserId] = new DuelPlayerState(invite.FromUserId, invite.FromDisplayName);
            duel.Players[invite.ToUserId] = new DuelPlayerState(invite.ToUserId, receiverDisplay);
            duel.Touch();

            DuelSessions[sessionId] = duel;

            if (invite.GameId == "chess-arena-duel")
            {
                ChessStates[sessionId] = new ChessMatchState(sessionId, invite.FromUserId, invite.ToUserId);
            }

            if (invite.GameId == "connect-four-arena-duel")
            {
                ConnectFourStates[sessionId] = new ConnectFourState(sessionId, invite.FromUserId, invite.ToUserId);
            }

            var senderAccepted = new InviteStatusPayload(invite.InviteId, "accepted", sessionId, receiverDisplay, invite.GameId);
            var receiverAccepted = new InviteStatusPayload(invite.InviteId, "accepted", sessionId, invite.FromDisplayName, invite.GameId);

            await _hubContext.Clients.User(invite.FromUserId).SendAsync("MinigameInviteStatus", senderAccepted);
            await _hubContext.Clients.User(invite.ToUserId).SendAsync("MinigameInviteStatus", receiverAccepted);

            return new InviteResponsePayload(invite.InviteId, "accepted", sessionId, invite.FromDisplayName, invite.GameId);
        }

        public async Task JoinDuelSession(string sessionId, string displayName)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new HubException("Session id is required.");
            }

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new HubException("Authentication required.");
            }

            if (!DuelSessions.TryGetValue(normalized, out var duel))
            {
                throw new HubException("Duel session was not found.");
            }

            if (!duel.Players.TryGetValue(userId, out var player))
            {
                throw new HubException("You are not part of this duel session.");
            }

            if (Context.Items.TryGetValue("duel", out var existing) && existing is string existingSession)
            {
                if (!string.Equals(existingSession, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    await LeaveDuelSession(existingSession);
                }
            }

            player.DisplayName = string.IsNullOrWhiteSpace(displayName) ? player.DisplayName : displayName.Trim();
            player.ConnectionId = Context.ConnectionId;
            player.Connected = true;
            duel.Touch();

            Context.Items["duel"] = normalized;

            await Groups.AddToGroupAsync(Context.ConnectionId, normalized);
            await BroadcastDuelSessionAsync(normalized, duel, force: true);

            if (duel.GameId == "chess-arena-duel" && ChessStates.TryGetValue(normalized, out var chessState))
            {
                await Clients.Caller.SendAsync("ChessStateUpdate", chessState.ToPayload());
            }

            if (duel.GameId == "connect-four-arena-duel" && ConnectFourStates.TryGetValue(normalized, out var connectFourState))
            {
                await Clients.Caller.SendAsync("ConnectFourStateUpdate", connectFourState.ToPayload());
            }
        }


        public async Task JoinDuelSessionAsViewer(string sessionId)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new HubException("Session id is required.");
            }

            if (!DuelSessions.TryGetValue(normalized, out var duel))
            {
                throw new HubException("Duel session was not found.");
            }

            if (Context.Items.TryGetValue("duel", out var existing) && existing is string existingSession)
            {
                if (!string.Equals(existingSession, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    await LeaveDuelSession(existingSession);
                }
            }

            Context.Items["duel"] = normalized;
            await Groups.AddToGroupAsync(Context.ConnectionId, normalized);
            await Clients.Caller.SendAsync("DuelSessionUpdate", duel.ToSnapshot());

            if (duel.GameId == "chess-arena-duel" && ChessStates.TryGetValue(normalized, out var chessState))
            {
                await Clients.Caller.SendAsync("ChessStateUpdate", chessState.ToPayload());
            }

            if (duel.GameId == "connect-four-arena-duel" && ConnectFourStates.TryGetValue(normalized, out var connectFourState))
            {
                await Clients.Caller.SendAsync("ConnectFourStateUpdate", connectFourState.ToPayload());
            }
        }

        public async Task LeaveDuelSession(string sessionId)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            if (DuelSessions.TryGetValue(normalized, out var duel))
            {
                var userId = GetCurrentUserId();
                if (!string.IsNullOrWhiteSpace(userId) && duel.Players.TryGetValue(userId, out var player))
                {
                    if (player.ConnectionId == Context.ConnectionId)
                    {
                        player.ConnectionId = string.Empty;
                        player.Connected = false;
                        duel.Touch();
                    }
                }

                await Groups.RemoveFromGroupAsync(Context.ConnectionId, normalized);
                await BroadcastDuelSessionAsync(normalized, duel, force: true);
            }

            Context.Items.Remove("duel");
            CleanupState();
        }

        public async Task<List<ShuffleMemberPayload>> GetShufflePool(string gameId)
        {
            CleanupState();
            var normalizedGame = NormalizeGameId(gameId);
            var entries = ShufflePool.Values
                .Where(entry => entry.GameId == normalizedGame)
                .OrderBy(entry => entry.JoinedAt)
                .Select(entry => new ShuffleMemberPayload(entry.UserId, entry.DisplayName, entry.JoinedAt))
                .ToList();

            return await Task.FromResult(entries);
        }

        public async Task SubscribeToShufflePool(string gameId)
        {
            var normalizedGame = NormalizeGameId(gameId);
            await Groups.AddToGroupAsync(Context.ConnectionId, ShuffleGroup(normalizedGame));
            await BroadcastShufflePoolAsync(normalizedGame);
        }

        public async Task UnsubscribeFromShufflePool(string gameId)
        {
            var normalizedGame = NormalizeGameId(gameId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ShuffleGroup(normalizedGame));
        }

        public async Task JoinShufflePool(string gameId)
        {
            CleanupState();

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new HubException("Authentication required.");
            }

            var normalizedGame = NormalizeGameId(gameId);
            var user = await _userManager.FindByIdAsync(userId);
            var displayName = user == null ? "Scholar" : BuildDisplayName(user);
            var entry = new ShufflePoolEntry(userId, displayName, normalizedGame, DateTimeOffset.UtcNow);

            ShufflePool[userId] = entry;
            Context.Items["shuffle"] = normalizedGame;

            await Groups.AddToGroupAsync(Context.ConnectionId, ShuffleGroup(normalizedGame));
            await BroadcastShufflePoolAsync(normalizedGame);
        }

        public async Task LeaveShufflePool(string gameId)
        {
            var normalizedGame = NormalizeGameId(gameId);
            var userId = GetCurrentUserId();
            if (!string.IsNullOrWhiteSpace(userId))
            {
                ShufflePool.TryRemove(userId, out _);
            }

            Context.Items.Remove("shuffle");
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ShuffleGroup(normalizedGame));
            await BroadcastShufflePoolAsync(normalizedGame);
        }

        public async Task<ShuffleMatchPayload?> StartShuffleMatch(string gameId)
        {
            CleanupState();

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new HubException("Authentication required.");
            }

            var normalizedGame = NormalizeGameId(gameId);
            var currentUser = await _userManager.FindByIdAsync(userId);
            var currentDisplay = currentUser == null ? "Scholar" : BuildDisplayName(currentUser);

            string? opponentId = null;
            ShufflePoolEntry? opponentEntry = null;

            lock (ShuffleLock)
            {
                var candidates = ShufflePool.Values
                    .Where(entry => entry.GameId == normalizedGame && entry.UserId != userId)
                    .ToList();

                if (candidates.Count > 0)
                {
                    var randomIndex = Random.Shared.Next(candidates.Count);
                    opponentEntry = candidates[randomIndex];
                    opponentId = opponentEntry.UserId;

                    ShufflePool.TryRemove(userId, out _);
                    ShufflePool.TryRemove(opponentId, out _);
                }
            }

            if (opponentEntry == null || opponentId == null)
            {
                return null;
            }

            var sessionId = CreateDuelSession(normalizedGame, userId, currentDisplay, opponentId, opponentEntry.DisplayName);

            var callerPayload = new ShuffleMatchPayload(sessionId, normalizedGame, opponentEntry.DisplayName);
            var opponentPayload = new ShuffleMatchPayload(sessionId, normalizedGame, currentDisplay);

            await _hubContext.Clients.User(userId).SendAsync("MinigameShuffleMatched", callerPayload);
            await _hubContext.Clients.User(opponentId).SendAsync("MinigameShuffleMatched", opponentPayload);
            await BroadcastShufflePoolAsync(normalizedGame);

            return callerPayload;
        }

        public async Task<ShuffleMatchPayload?> RequestShuffleMatchWith(string gameId, string opponentUserId)
        {
            CleanupState();

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new HubException("Authentication required.");
            }

            if (string.IsNullOrWhiteSpace(opponentUserId))
            {
                throw new HubException("Opponent user id is required.");
            }

            var normalizedGame = NormalizeGameId(gameId);
            var currentUser = await _userManager.FindByIdAsync(userId);
            var currentDisplay = currentUser == null ? "Scholar" : BuildDisplayName(currentUser);

            ShufflePoolEntry? opponentEntry = null;

            lock (ShuffleLock)
            {
                if (ShufflePool.TryGetValue(opponentUserId, out var entry) && entry.GameId == normalizedGame)
                {
                    opponentEntry = entry;
                    ShufflePool.TryRemove(userId, out _);
                    ShufflePool.TryRemove(opponentUserId, out _);
                }
            }

            if (opponentEntry == null)
            {
                throw new HubException("Opponent is no longer waiting.");
            }

            var sessionId = CreateDuelSession(normalizedGame, userId, currentDisplay, opponentEntry.UserId, opponentEntry.DisplayName);

            var callerPayload = new ShuffleMatchPayload(sessionId, normalizedGame, opponentEntry.DisplayName);
            var opponentPayload = new ShuffleMatchPayload(sessionId, normalizedGame, currentDisplay);

            await _hubContext.Clients.User(userId).SendAsync("MinigameShuffleMatched", callerPayload);
            await _hubContext.Clients.User(opponentEntry.UserId).SendAsync("MinigameShuffleMatched", opponentPayload);
            await BroadcastShufflePoolAsync(normalizedGame);

            return callerPayload;
        }

        public async Task SendSpectatorReaction(string sessionId, string reaction)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new HubException("Session id is required.");
            }

            if (string.IsNullOrWhiteSpace(reaction))
            {
                return;
            }

            if (!DuelSessions.ContainsKey(normalized))
            {
                throw new HubException("Duel session was not found.");
            }

            var userId = GetCurrentUserId() ?? string.Empty;
            var displayName = "Scholar";
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user != null)
                {
                    displayName = BuildDisplayName(user);
                }
            }

            await _hubContext.Clients.Group(normalized).SendAsync("SpectatorReaction", new SpectatorReactionPayload(normalized, reaction.Trim(), displayName));
        }


        public async Task<List<DuelSessionSnapshot>> GetActiveDuelSessions()
        {
            CleanupState();
            var snapshots = DuelSessions.Values
                .OrderByDescending(entry => entry.LastActiveAt)
                .Select(entry => entry.ToSnapshot())
                .ToList();

            return await Task.FromResult(snapshots);
        }

        public async Task SubscribeToActiveDuelUpdates()
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, "active-duels");
        }

        public async Task UnsubscribeFromActiveDuelUpdates()
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "active-duels");
        }

        public async Task StartDuelSession(string sessionId)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new HubException("Authentication required.");
            }

            if (!DuelSessions.TryGetValue(normalized, out var duel))
            {
                throw new HubException("Duel session was not found.");
            }

            if (!duel.Players.ContainsKey(userId))
            {
                throw new HubException("Only invited players can start this round.");
            }

            bool usesBoost;
            bool isChessGame;
            ChessMatchState? chessState = null;
            bool isConnectFourGame;
            ConnectFourState? connectFourState = null;

            lock (duel.SyncRoot)
            {
                if (duel.IsRunning)
                {
                    return;
                }

                var connectedPlayers = duel.Players.Values.Count(entry => entry.Connected);
                if (connectedPlayers < 2)
                {
                    throw new HubException("Both players must be connected to start.");
                }

                duel.IsRunning = true;
                duel.RoundToken++;
                duel.RoundEndsAt = DateTimeOffset.UtcNow.AddSeconds(duel.RoundSeconds);
                usesBoost = duel.GameId == "signal-smash-duel";
                isChessGame = duel.GameId == "chess-arena-duel";
                isConnectFourGame = duel.GameId == "connect-four-arena-duel";
                duel.BoostedUserId = usesBoost ? PickNextBoostedUser(duel, null) : null;

                foreach (var player in duel.Players.Values)
                {
                    player.Score = 0;
                }

                duel.Touch();
            }

            if (isChessGame)
            {
                chessState = ChessStates.AddOrUpdate(
                    normalized,
                    _ => CreateChessState(normalized, duel),
                    (_, existing) =>
                    {
                        lock (existing.SyncRoot)
                        {
                            existing.ResetForNewRound();
                            return existing;
                        }
                    });
            }

            if (isConnectFourGame)
            {
                connectFourState = ConnectFourStates.AddOrUpdate(
                    normalized,
                    _ => CreateConnectFourState(normalized, duel),
                    (_, existing) =>
                    {
                        lock (existing.SyncRoot)
                        {
                            existing.ResetForNewRound();
                            return existing;
                        }
                    });
            }

            await BroadcastDuelSessionAsync(normalized, duel, force: true);

            if (chessState != null)
            {
                await _hubContext.Clients.Group(normalized).SendAsync("ChessStateUpdate", chessState.ToPayload());
            }

            if (connectFourState != null)
            {
                await _hubContext.Clients.Group(normalized).SendAsync("ConnectFourStateUpdate", connectFourState.ToPayload());
            }

            _ = Task.Run(async () =>
            {
                var token = duel.RoundToken;

                if (usesBoost)
                {
                    while (true)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(3));

                        bool shouldContinue;
                        lock (duel.SyncRoot)
                        {
                            shouldContinue = duel.IsRunning && duel.RoundToken == token;
                            if (!shouldContinue)
                            {
                                break;
                            }

                            if (duel.RoundEndsAt <= DateTimeOffset.UtcNow)
                            {
                                duel.IsRunning = false;
                                duel.RoundEndsAt = null;
                                duel.BoostedUserId = null;
                                duel.Touch();
                                shouldContinue = false;
                            }
                            else
                            {
                                duel.BoostedUserId = PickNextBoostedUser(duel, duel.BoostedUserId);
                                duel.Touch();
                            }
                        }

                        await BroadcastDuelSessionAsync(normalized, duel, force: true);

                        if (!shouldContinue)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(duel.RoundSeconds));
                    var shouldBroadcast = false;
                    lock (duel.SyncRoot)
                    {
                        if (duel.IsRunning && duel.RoundToken == token)
                        {
                            duel.IsRunning = false;
                            duel.RoundEndsAt = null;
                            duel.BoostedUserId = null;
                            duel.Touch();
                            shouldBroadcast = true;
                        }
                    }

                    if (shouldBroadcast)
                    {
                        await BroadcastDuelSessionAsync(normalized, duel, force: true);
                    }
                }
            });
        }

        public async Task DuelTap(string sessionId)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return;

            if (!DuelSessions.TryGetValue(normalized, out var duel)) return;
            if (!duel.IsRunning) return;
            if (duel.GameId != "signal-smash-duel") return;

            if (duel.Players.TryGetValue(userId, out var player) && player.Connected)
            {
                var points = duel.BoostedUserId == userId ? 2 : 1;
                player.Score += points;
                duel.Touch();
                await BroadcastDuelSessionAsync(normalized, duel, force: false);
            }
        }

        public async Task UpdateRunnerDistance(string sessionId, int distance)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return;

            if (!DuelSessions.TryGetValue(normalized, out var duel)) return;
            if (!duel.IsRunning) return;
            if (duel.GameId != "neon-runner-duel") return;

            if (duel.Players.TryGetValue(userId, out var player) && player.Connected)
            {
                player.Score = Math.Max(player.Score, distance);
                duel.Touch();
                await BroadcastDuelSessionAsync(normalized, duel, force: false);
            }
        }

        public async Task SubmitDuelScore(string sessionId, int score)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return;

            if (!DuelSessions.TryGetValue(normalized, out var duel)) return;
            if (!duel.IsRunning) return;
            if (duel.GameId == "signal-smash-duel") return;

            if (duel.Players.TryGetValue(userId, out var player) && player.Connected)
            {
                player.Score = Math.Max(player.Score, score);
                duel.Touch();
                await BroadcastDuelSessionAsync(normalized, duel, force: false);
            }
        }

        public ChessStatePayload GetChessState(string sessionId)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new HubException("Session id is required.");
            }

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new HubException("Authentication required.");
            }

            if (!DuelSessions.TryGetValue(normalized, out var duel) || duel.GameId != "chess-arena-duel")
            {
                throw new HubException("Chess session was not found.");
            }

            if (!duel.Players.ContainsKey(userId))
            {
                throw new HubException("You are not part of this chess session.");
            }

            var chessState = ChessStates.AddOrUpdate(
                normalized,
                _ => CreateChessState(normalized, duel),
                (_, existing) => existing
            );

            return chessState.ToPayload();
        }

        public async Task SubmitChessMove(
            string sessionId,
            string fen,
            string pgn,
            string turn,
            int ply,
            string? lastMove,
            string status,
            string? winnerUserId)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return;

            if (!DuelSessions.TryGetValue(normalized, out var duel)) return;
            if (!duel.IsRunning) return;
            if (duel.GameId != "chess-arena-duel") return;
            if (!duel.Players.ContainsKey(userId)) return;

            var chessState = ChessStates.AddOrUpdate(
                normalized,
                _ => CreateChessState(normalized, duel),
                (_, existing) => existing
            );

            var normalizedStatus = NormalizeChessStatus(status);

            lock (chessState.SyncRoot)
            {
                chessState.ApplyMove(fen, pgn, turn, ply, lastMove, normalizedStatus, winnerUserId);
            }

            if (normalizedStatus == "checkmate" && !string.IsNullOrWhiteSpace(winnerUserId) && duel.Players.TryGetValue(winnerUserId, out var winner))
            {
                winner.Score = 1;
                duel.IsRunning = false;
                duel.RoundEndsAt = null;
            }
            else if (normalizedStatus == "draw")
            {
                duel.IsRunning = false;
                duel.RoundEndsAt = null;
            }

            duel.Touch();
            await _hubContext.Clients.Group(normalized).SendAsync("ChessStateUpdate", chessState.ToPayload());
            await BroadcastDuelSessionAsync(normalized, duel, force: true);
        }

        public async Task ResetChessBoard(string sessionId)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return;

            if (!DuelSessions.TryGetValue(normalized, out var duel)) return;
            if (duel.GameId != "chess-arena-duel") return;
            if (!duel.Players.ContainsKey(userId)) return;

            var chessState = ChessStates.AddOrUpdate(
                normalized,
                _ => CreateChessState(normalized, duel),
                (_, existing) => existing
            );

            lock (chessState.SyncRoot)
            {
                chessState.ResetForNewRound("waiting");
            }

            duel.IsRunning = false;
            duel.RoundEndsAt = null;
            foreach (var player in duel.Players.Values)
            {
                player.Score = 0;
            }
            duel.Touch();

            await _hubContext.Clients.Group(normalized).SendAsync("ChessStateUpdate", chessState.ToPayload());
            await BroadcastDuelSessionAsync(normalized, duel, force: true);
        }


        public ConnectFourStatePayload GetConnectFourState(string sessionId)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new HubException("Session id is required.");
            }

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new HubException("Authentication required.");
            }

            if (!DuelSessions.TryGetValue(normalized, out var duel) || duel.GameId != "connect-four-arena-duel")
            {
                throw new HubException("Connect four session was not found.");
            }

            if (!duel.Players.ContainsKey(userId))
            {
                throw new HubException("You are not part of this connect four session.");
            }

            var connectFourState = ConnectFourStates.AddOrUpdate(
                normalized,
                _ => CreateConnectFourState(normalized, duel),
                (_, existing) => existing
            );

            return connectFourState.ToPayload();
        }

        public async Task SubmitConnectFourMove(string sessionId, int column)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return;

            if (!DuelSessions.TryGetValue(normalized, out var duel)) return;
            if (!duel.IsRunning) return;
            if (duel.GameId != "connect-four-arena-duel") return;
            if (!duel.Players.ContainsKey(userId)) return;

            var connectFourState = ConnectFourStates.AddOrUpdate(
                normalized,
                _ => CreateConnectFourState(normalized, duel),
                (_, existing) => existing
            );

            bool moved;
            lock (connectFourState.SyncRoot)
            {
                moved = connectFourState.DropDisc(column, userId);
            }

            if (!moved) return;

            if (connectFourState.Status == "won" && !string.IsNullOrWhiteSpace(connectFourState.WinnerUserId) && duel.Players.TryGetValue(connectFourState.WinnerUserId, out var winner))
            {
                winner.Score = 1;
                duel.IsRunning = false;
                duel.RoundEndsAt = null;
            }
            else if (connectFourState.Status == "draw")
            {
                duel.IsRunning = false;
                duel.RoundEndsAt = null;
            }

            duel.Touch();
            await _hubContext.Clients.Group(normalized).SendAsync("ConnectFourStateUpdate", connectFourState.ToPayload());
            await BroadcastDuelSessionAsync(normalized, duel, force: true);
        }

        public async Task ResetConnectFourBoard(string sessionId)
        {
            var normalized = NormalizeCode(sessionId);
            if (string.IsNullOrWhiteSpace(normalized)) return;

            var userId = GetCurrentUserId();
            if (string.IsNullOrWhiteSpace(userId)) return;

            if (!DuelSessions.TryGetValue(normalized, out var duel)) return;
            if (duel.GameId != "connect-four-arena-duel") return;
            if (!duel.Players.ContainsKey(userId)) return;

            var connectFourState = ConnectFourStates.AddOrUpdate(
                normalized,
                _ => CreateConnectFourState(normalized, duel),
                (_, existing) => existing
            );

            lock (connectFourState.SyncRoot)
            {
                connectFourState.ResetForNewRound("waiting");
            }

            duel.IsRunning = false;
            duel.RoundEndsAt = null;
            foreach (var player in duel.Players.Values)
            {
                player.Score = 0;
            }
            duel.Touch();

            await _hubContext.Clients.Group(normalized).SendAsync("ConnectFourStateUpdate", connectFourState.ToPayload());
            await BroadcastDuelSessionAsync(normalized, duel, force: true);
        }
        private async Task BroadcastRoomAsync(string roomCode, RoomState room, bool force)
        {
            var now = DateTimeOffset.UtcNow;
            if (!force && now - room.LastBroadcastAt < BroadcastThrottle) return;

            room.LastBroadcastAt = now;
            await _hubContext.Clients.Group(roomCode).SendAsync("RoomUpdate", room.ToSnapshot());
            CleanupState();
        }

        private async Task BroadcastDuelSessionAsync(string sessionId, DuelSession duel, bool force)
        {
            var now = DateTimeOffset.UtcNow;
            if (!force && now - duel.LastBroadcastAt < BroadcastThrottle) return;

            duel.LastBroadcastAt = now;
            await _hubContext.Clients.Group(sessionId).SendAsync("DuelSessionUpdate", duel.ToSnapshot());
            await _hubContext.Clients.Group("active-duels").SendAsync("ActiveDuelUpdate", duel.ToSnapshot());
            CleanupState();
        }

        private static string ShuffleGroup(string gameId)
        {
            return $"shuffle:{gameId}";
        }

        private async Task BroadcastShufflePoolAsync(string gameId)
        {
            var entries = ShufflePool.Values
                .Where(entry => entry.GameId == gameId)
                .OrderBy(entry => entry.JoinedAt)
                .Select(entry => new ShuffleMemberPayload(entry.UserId, entry.DisplayName, entry.JoinedAt))
                .ToList();

            await _hubContext.Clients.Group(ShuffleGroup(gameId)).SendAsync("ShufflePoolUpdate", gameId, entries);
        }

        private async Task RemoveFromShufflePoolAsync(string userId)
        {
            if (ShufflePool.TryRemove(userId, out var entry))
            {
                await BroadcastShufflePoolAsync(entry.GameId);
            }
        }

        private string CreateDuelSession(string gameId, string firstUserId, string firstDisplayName, string secondUserId, string secondDisplayName)
        {
            var roundSeconds = gameId switch
            {
                "neon-runner-duel" => NeonRunnerRoundSeconds,
                "card-clash-duel" => CardClashRoundSeconds,
                "knight-tactics-duel" => KnightTacticsRoundSeconds,
                "chess-arena-duel" => ChessArenaRoundSeconds,
                "connect-four-arena-duel" => ConnectFourRoundSeconds,
                _ => SignalSmashRoundSeconds,
            };

            var sessionId = GenerateSessionId();
            var duel = new DuelSession(sessionId, roundSeconds, gameId);

            duel.Players[firstUserId] = new DuelPlayerState(firstUserId, firstDisplayName);
            duel.Players[secondUserId] = new DuelPlayerState(secondUserId, secondDisplayName);
            duel.Touch();

            DuelSessions[sessionId] = duel;

            if (gameId == "chess-arena-duel")
            {
                ChessStates[sessionId] = new ChessMatchState(sessionId, firstUserId, secondUserId);
            }

            if (gameId == "connect-four-arena-duel")
            {
                ConnectFourStates[sessionId] = new ConnectFourState(sessionId, firstUserId, secondUserId);
            }

            return sessionId;
        }

        private static void CleanupState()
        {
            var now = DateTimeOffset.UtcNow;

            foreach (var invite in Invites)
            {
                if (invite.Value.CreatedAt.Add(InviteTtl) <= now)
                {
                    Invites.TryRemove(invite.Key, out _);
                }
            }

            foreach (var room in Rooms)
            {
                if (room.Value.Players.IsEmpty || now - room.Value.LastActiveAt > RoomTtl)
                {
                    Rooms.TryRemove(room.Key, out _);
                }
            }

            foreach (var duel in DuelSessions)
            {
                var session = duel.Value;
                var stale = now - session.LastActiveAt > DuelTtl;
                var abandoned = !session.IsRunning && session.Players.Values.All(player => !player.Connected);
                if (stale || abandoned)
                {
                    DuelSessions.TryRemove(duel.Key, out _);
                }
            }

            foreach (var chess in ChessStates)
            {
                if (!DuelSessions.ContainsKey(chess.Key))
                {
                    ChessStates.TryRemove(chess.Key, out _);
                }
            }

            foreach (var connect in ConnectFourStates)
            {
                if (!DuelSessions.ContainsKey(connect.Key))
                {
                    ConnectFourStates.TryRemove(connect.Key, out _);
                }
            }

            foreach (var entry in ShufflePool)
            {
                if (entry.Value.JoinedAt.Add(ShuffleTtl) <= now)
                {
                    ShufflePool.TryRemove(entry.Key, out _);
                }
            }
        }

        private static string NormalizeCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Trim().ToUpperInvariant();
        }

        private static string NormalizeGameId(string? gameId)
        {
            if (string.Equals(gameId, "neon-runner-duel", StringComparison.OrdinalIgnoreCase)) return "neon-runner-duel";
            if (string.Equals(gameId, "card-clash-duel", StringComparison.OrdinalIgnoreCase)) return "card-clash-duel";
            if (string.Equals(gameId, "knight-tactics-duel", StringComparison.OrdinalIgnoreCase)) return "knight-tactics-duel";
            if (string.Equals(gameId, "chess-arena-duel", StringComparison.OrdinalIgnoreCase)) return "chess-arena-duel";
            if (string.Equals(gameId, "connect-four-arena-duel", StringComparison.OrdinalIgnoreCase)) return "connect-four-arena-duel";
            return "signal-smash-duel";
        }

        private static string GenerateSessionId()
        {
            return Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        }

        private string? GetCurrentUserId()
        {
            return Context.UserIdentifier
                ?? Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? Context.User?.FindFirstValue(JwtRegisteredClaimNames.Sub);
        }

        private static string? PickNextBoostedUser(DuelSession duel, string? previous)
        {
            var candidates = duel.Players.Values
                .Where(entry => entry.Connected)
                .OrderBy(entry => entry.UserId)
                .Select(entry => entry.UserId)
                .ToList();

            if (candidates.Count == 0)
            {
                return duel.Players.Keys.OrderBy(value => value).FirstOrDefault();
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (!string.IsNullOrWhiteSpace(previous))
            {
                var index = candidates.IndexOf(previous);
                if (index >= 0)
                {
                    return candidates[(index + 1) % candidates.Count];
                }
            }

            return candidates[0];
        }

        private static ChessMatchState CreateChessState(string sessionId, DuelSession duel)
        {
            var ordered = duel.Players.Keys.OrderBy(value => value).ToList();
            var whiteUserId = ordered.ElementAtOrDefault(0) ?? string.Empty;
            var blackUserId = ordered.ElementAtOrDefault(1) ?? whiteUserId;
            return new ChessMatchState(sessionId, whiteUserId, blackUserId);
        }

        private static ConnectFourState CreateConnectFourState(string sessionId, DuelSession duel)
        {
            var ordered = duel.Players.Keys.OrderBy(value => value).ToList();
            var playerOneUserId = ordered.ElementAtOrDefault(0) ?? string.Empty;
            var playerTwoUserId = ordered.ElementAtOrDefault(1) ?? playerOneUserId;
            return new ConnectFourState(sessionId, playerOneUserId, playerTwoUserId);
        }

        private static string NormalizeChessStatus(string? status)
        {
            if (string.Equals(status, "checkmate", StringComparison.OrdinalIgnoreCase)) return "checkmate";
            if (string.Equals(status, "draw", StringComparison.OrdinalIgnoreCase)) return "draw";
            if (string.Equals(status, "check", StringComparison.OrdinalIgnoreCase)) return "check";
            return "playing";
        }

        private sealed class RoomState
        {
            public RoomState(int roundSeconds)
            {
                RoundSeconds = roundSeconds;
            }

            public ConcurrentDictionary<string, PlayerState> Players { get; } = new();
            public bool IsRunning { get; set; }
            public DateTimeOffset? RoundEndsAt { get; set; }
            public DateTimeOffset LastActiveAt { get; private set; } = DateTimeOffset.UtcNow;
            public DateTimeOffset LastBroadcastAt { get; set; } = DateTimeOffset.MinValue;
            public int RoundSeconds { get; }
            public long RoundToken { get; set; }
            public object SyncRoot { get; } = new();

            public void Touch() => LastActiveAt = DateTimeOffset.UtcNow;

            public RoomSnapshot ToSnapshot()
            {
                var players = Players.Values
                    .OrderByDescending(entry => entry.Score)
                    .ThenBy(entry => entry.DisplayName)
                    .Select(entry => new PlayerSnapshot(entry.DisplayName, entry.Score))
                    .ToList();

                return new RoomSnapshot(IsRunning, RoundEndsAt, RoundSeconds, players);
            }
        }

        private sealed class PlayerState
        {
            public string ConnectionId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public int Score { get; set; }
        }

        private sealed class DuelInvite
        {
            public DuelInvite(string inviteId, string fromUserId, string toUserId, string fromDisplayName, DateTimeOffset createdAt, string gameId)
            {
                InviteId = inviteId;
                FromUserId = fromUserId;
                ToUserId = toUserId;
                FromDisplayName = fromDisplayName;
                CreatedAt = createdAt;
                GameId = gameId;
            }

            public string InviteId { get; }
            public string FromUserId { get; }
            public string ToUserId { get; }
            public string FromDisplayName { get; }
            public DateTimeOffset CreatedAt { get; }
            public string GameId { get; }
        }

        private sealed class DuelSession
        {
            public DuelSession(string sessionId, int roundSeconds, string gameId)
            {
                SessionId = sessionId;
                RoundSeconds = roundSeconds;
                GameId = gameId;
            }

            public string SessionId { get; }
            public string GameId { get; }
            public ConcurrentDictionary<string, DuelPlayerState> Players { get; } = new();
            public bool IsRunning { get; set; }
            public string? BoostedUserId { get; set; }
            public DateTimeOffset? RoundEndsAt { get; set; }
            public DateTimeOffset LastActiveAt { get; private set; } = DateTimeOffset.UtcNow;
            public DateTimeOffset LastBroadcastAt { get; set; } = DateTimeOffset.MinValue;
            public long RoundToken { get; set; }
            public int RoundSeconds { get; }
            public object SyncRoot { get; } = new();

            public void Touch() => LastActiveAt = DateTimeOffset.UtcNow;

            public DuelSessionSnapshot ToSnapshot()
            {
                var orderedPlayers = Players.Values
                    .OrderByDescending(entry => entry.Score)
                    .ThenBy(entry => entry.DisplayName)
                    .Select(entry => new DuelPlayerSnapshot(entry.UserId, entry.DisplayName, entry.Score, entry.Connected))
                    .ToList();

                return new DuelSessionSnapshot(SessionId, GameId, IsRunning, RoundEndsAt, RoundSeconds, BoostedUserId, orderedPlayers);
            }
        }

        private sealed class DuelPlayerState
        {
            public DuelPlayerState(string userId, string displayName)
            {
                UserId = userId;
                DisplayName = displayName;
            }

            public string UserId { get; }
            public string DisplayName { get; set; }
            public string ConnectionId { get; set; } = string.Empty;
            public bool Connected { get; set; }
            public int Score { get; set; }
        }

        private sealed class ChessMatchState
        {
            public ChessMatchState(string sessionId, string whiteUserId, string blackUserId)
            {
                SessionId = sessionId;
                WhiteUserId = whiteUserId;
                BlackUserId = blackUserId;
                ResetForNewRound("waiting");
            }

            public string SessionId { get; }
            public string WhiteUserId { get; private set; }
            public string BlackUserId { get; private set; }
            public string Fen { get; private set; } = "start";
            public string Pgn { get; private set; } = string.Empty;
            public string Turn { get; private set; } = "w";
            public int Ply { get; private set; }
            public string Status { get; private set; } = "waiting";
            public string? LastMove { get; private set; }
            public string? WinnerUserId { get; private set; }
            public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
            public object SyncRoot { get; } = new();

            public void ResetForNewRound(string status = "playing")
            {
                Fen = "start";
                Pgn = string.Empty;
                Turn = "w";
                Ply = 0;
                Status = status;
                LastMove = null;
                WinnerUserId = null;
                UpdatedAt = DateTimeOffset.UtcNow;
            }

            public void ApplyMove(string fen, string pgn, string turn, int ply, string? lastMove, string status, string? winnerUserId)
            {
                Fen = string.IsNullOrWhiteSpace(fen) ? "start" : fen;
                Pgn = pgn ?? string.Empty;
                Turn = string.Equals(turn, "b", StringComparison.OrdinalIgnoreCase) ? "b" : "w";
                Ply = Math.Max(0, ply);
                LastMove = string.IsNullOrWhiteSpace(lastMove) ? null : lastMove;
                Status = status;
                WinnerUserId = string.IsNullOrWhiteSpace(winnerUserId) ? null : winnerUserId;
                UpdatedAt = DateTimeOffset.UtcNow;
            }

            public ChessStatePayload ToPayload()
            {
                return new ChessStatePayload(
                    SessionId,
                    WhiteUserId,
                    BlackUserId,
                    Fen,
                    Pgn,
                    Turn,
                    Ply,
                    Status,
                    LastMove,
                    WinnerUserId,
                    UpdatedAt
                );
            }
        }


        private sealed class ConnectFourState
        {
            private readonly int[,] _board = new int[6, 7];

            public ConnectFourState(string sessionId, string playerOneUserId, string playerTwoUserId)
            {
                SessionId = sessionId;
                PlayerOneUserId = playerOneUserId;
                PlayerTwoUserId = playerTwoUserId;
                ResetForNewRound("waiting");
            }

            public string SessionId { get; }
            public string PlayerOneUserId { get; private set; }
            public string PlayerTwoUserId { get; private set; }
            public string TurnUserId { get; private set; } = string.Empty;
            public string Status { get; private set; } = "waiting";
            public string? WinnerUserId { get; private set; }
            public int? LastColumn { get; private set; }
            public int MoveCount { get; private set; }
            public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
            public object SyncRoot { get; } = new();

            public void ResetForNewRound(string status = "playing")
            {
                for (var row = 0; row < 6; row += 1)
                {
                    for (var col = 0; col < 7; col += 1)
                    {
                        _board[row, col] = 0;
                    }
                }

                TurnUserId = PlayerOneUserId;
                Status = status;
                WinnerUserId = null;
                LastColumn = null;
                MoveCount = 0;
                UpdatedAt = DateTimeOffset.UtcNow;
            }

            public bool DropDisc(int column, string userId)
            {
                if (Status != "playing") return false;
                if (!string.Equals(TurnUserId, userId, StringComparison.OrdinalIgnoreCase)) return false;
                if (column < 0 || column > 6) return false;

                var token = string.Equals(userId, PlayerOneUserId, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
                var targetRow = -1;

                for (var row = 5; row >= 0; row -= 1)
                {
                    if (_board[row, column] == 0)
                    {
                        targetRow = row;
                        break;
                    }
                }

                if (targetRow < 0) return false;

                _board[targetRow, column] = token;
                MoveCount += 1;
                LastColumn = column;
                UpdatedAt = DateTimeOffset.UtcNow;

                if (HasWinningLine(targetRow, column, token))
                {
                    Status = "won";
                    WinnerUserId = userId;
                    return true;
                }

                if (MoveCount >= 42)
                {
                    Status = "draw";
                    WinnerUserId = null;
                    return true;
                }

                TurnUserId = string.Equals(userId, PlayerOneUserId, StringComparison.OrdinalIgnoreCase) ? PlayerTwoUserId : PlayerOneUserId;
                return true;
            }

            private bool HasWinningLine(int row, int col, int token)
            {
                return CountDirection(row, col, token, 1, 0) + CountDirection(row, col, token, -1, 0) - 1 >= 4
                    || CountDirection(row, col, token, 0, 1) + CountDirection(row, col, token, 0, -1) - 1 >= 4
                    || CountDirection(row, col, token, 1, 1) + CountDirection(row, col, token, -1, -1) - 1 >= 4
                    || CountDirection(row, col, token, 1, -1) + CountDirection(row, col, token, -1, 1) - 1 >= 4;
            }

            private int CountDirection(int row, int col, int token, int rowStep, int colStep)
            {
                var count = 0;
                var currentRow = row;
                var currentCol = col;

                while (currentRow >= 0 && currentRow < 6 && currentCol >= 0 && currentCol < 7 && _board[currentRow, currentCol] == token)
                {
                    count += 1;
                    currentRow += rowStep;
                    currentCol += colStep;
                }

                return count;
            }

            public ConnectFourStatePayload ToPayload()
            {
                var chars = new char[42];
                var index = 0;
                for (var row = 0; row < 6; row += 1)
                {
                    for (var col = 0; col < 7; col += 1)
                    {
                        chars[index] = (char)('0' + _board[row, col]);
                        index += 1;
                    }
                }

                return new ConnectFourStatePayload(
                    SessionId,
                    PlayerOneUserId,
                    PlayerTwoUserId,
                    new string(chars),
                    TurnUserId,
                    Status,
                    WinnerUserId,
                    LastColumn,
                    MoveCount,
                    UpdatedAt
                );
            }
        }

        public sealed record DuelInvitePayload(
            string InviteId,
            string FromUserId,
            string FromDisplayName,
            string GameId,
            DateTimeOffset ExpiresAt
        );

        public sealed record InviteStatusPayload(
            string InviteId,
            string Status,
            string? SessionId,
            string? OpponentDisplayName,
            string GameId
        );

        public sealed record InviteResponsePayload(
            string InviteId,
            string Status,
            string? SessionId,
            string? OpponentDisplayName,
            string GameId
        );

        public sealed record PlayerSnapshot(string DisplayName, int Score);

        public sealed record RoomSnapshot(
            bool IsRunning,
            DateTimeOffset? RoundEndsAt,
            int RoundSeconds,
            IReadOnlyList<PlayerSnapshot> Players
        );

        public sealed record DuelPlayerSnapshot(
            string UserId,
            string DisplayName,
            int Score,
            bool Connected
        );

        public sealed record DuelSessionSnapshot(
            string SessionId,
            string GameId,
            bool IsRunning,
            DateTimeOffset? RoundEndsAt,
            int RoundSeconds,
            string? BoostedUserId,
            IReadOnlyList<DuelPlayerSnapshot> Players
        );

        public sealed record ShuffleMemberPayload(
            string UserId,
            string DisplayName,
            DateTimeOffset JoinedAt
        );

        public sealed record ShuffleMatchPayload(
            string SessionId,
            string GameId,
            string OpponentDisplayName
        );

        public sealed record SpectatorReactionPayload(
            string SessionId,
            string Reaction,
            string FromDisplayName
        );

        public sealed record ShufflePoolEntry(
            string UserId,
            string DisplayName,
            string GameId,
            DateTimeOffset JoinedAt
        );

        public sealed record ChessStatePayload(
            string SessionId,
            string WhiteUserId,
            string BlackUserId,
            string Fen,
            string Pgn,
            string Turn,
            int Ply,
            string Status,
            string? LastMove,
            string? WinnerUserId,
            DateTimeOffset UpdatedAt
        );

        public sealed record ConnectFourStatePayload(
            string SessionId,
            string PlayerOneUserId,
            string PlayerTwoUserId,
            string Board,
            string TurnUserId,
            string Status,
            string? WinnerUserId,
            int? LastColumn,
            int MoveCount,
            DateTimeOffset UpdatedAt
        );
    }
}





