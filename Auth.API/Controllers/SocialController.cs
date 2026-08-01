using System.Security.Claims;
using Auth.Models.Constants;
using Auth.Models.Data;
using Auth.Models.Entities.Notifications;
using Auth.Models.Response;
using Auth.Services.Interfaces.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Auth.API.Controllers
{
    /// <summary>
    /// The two lightest things one scholar can do to another: poke them, or invite them to a game.
    ///
    /// ── Why these are HTTP and not hub calls ─────────────────────────────────
    ///
    /// The minigames hub already has an invite method, and it works — but only from a screen that
    /// has negotiated a connection to that hub, which in practice means from inside a duel game.
    /// The whole point of this is to invite someone from wherever you happen to notice they are
    /// online, which is the presence dropdown in the navbar.
    ///
    /// Going through the notification service instead of the hub also means the invite is
    /// *persisted*. A hub message reaches people who are looking at the app right now and is lost
    /// on everyone else; a notification is waiting in the bell when they come back, and is pushed
    /// in realtime to those who are connected. For an invite that is the difference between a
    /// feature and a coin flip.
    /// </summary>
    [Route("api/social")]
    [ApiController]
    [Authorize]
    public class SocialController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notifications;
        private readonly ILogger<SocialController> _logger;

        /// <summary>
        /// How long one poke suppresses the next between the same two people.
        ///
        /// Not implemented as a counter — the notification service already dedupes on
        /// <c>DedupeKey</c>, so bucketing the clock into five-minute slices and putting the bucket
        /// in the key gets rate limiting for free. Poking twice in a slice silently does nothing,
        /// which is exactly the desired behaviour: the sender is not told off, the receiver is not
        /// spammed.
        /// </summary>
        private static readonly TimeSpan PokeCooldown = TimeSpan.FromMinutes(5);

        /// <summary>Games worth inviting someone to — the ones that need a second player.</summary>
        private static readonly Dictionary<string, string> DuelGames = new(StringComparer.Ordinal)
        {
            ["chess-arena-duel"] = "Chess Arena",
            ["connect-four-arena-duel"] = "Connect Four Arena",
            ["knight-tactics-duel"] = "Knight Tactics",
        };

        public SocialController(
            ApplicationDbContext context,
            INotificationService notifications,
            ILogger<SocialController> logger)
        {
            _context = context;
            _notifications = notifications;
            _logger = logger;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        /// <summary>The games the invite modal offers. Served so the list lives in one place.</summary>
        [HttpGet("duel-games")]
        public ActionResult<ApiResponse<List<DuelGameDto>>> GetDuelGames() =>
            Ok(ApiResponse<List<DuelGameDto>>.SuccessResponse(
                DuelGames.Select(g => new DuelGameDto { GameId = g.Key, Name = g.Value }).ToList(),
                "Duel games."));

        [HttpPost("poke/{targetUserId}")]
        [EnableRateLimiting("ip-only")]
        public async Task<ActionResult<ApiResponse<bool>>> Poke(string targetUserId, CancellationToken cancellationToken)
        {
            var senderId = GetUserId();

            if (string.Equals(senderId, targetUserId, StringComparison.Ordinal))
                return BadRequest(ApiResponse<bool>.ErrorResponse("You cannot poke yourself."));

            var sender = await LoadNameAsync(senderId, cancellationToken);
            if (sender is null) return Unauthorized(ApiResponse<bool>.ErrorResponse("Unknown sender."));

            var targetExists = await _context.Users
                .AnyAsync(u => u.Id == targetUserId && u.IsActive, cancellationToken);

            if (!targetExists) return NotFound(ApiResponse<bool>.ErrorResponse("That person is not available."));

            var bucket = DateTimeOffset.UtcNow.Ticks / PokeCooldown.Ticks;

            var created = await _notifications.CreateAsync(new CreateNotificationRequest
            {
                UserId = targetUserId,
                MessageKey = NotificationKeys.Poked,
                Params = new Dictionary<string, string> { ["fromName"] = sender },

                // Several people poking at once collapse into one entry rather than stacking.
                CollapseKey = "poke",

                // The cooldown, expressed as a key the dedupe already understands.
                DedupeKey = $"poke:{senderId}:{targetUserId}:{bucket}",

                // A poke is a wave, not news. Emailing one would be absurd.
                WantsEmail = false,
                ActionUrl = "/minigames",
            }, cancellationToken);

            // Reported honestly rather than pretending. The UI shows a quieter confirmation when
            // the poke was swallowed by the cooldown, so nobody wonders why nothing happened.
            return Ok(ApiResponse<bool>.SuccessResponse(created is not null, created is not null ? "Poked." : "Already poked recently."));
        }

        [HttpPost("invite")]
        [EnableRateLimiting("ip-only")]
        public async Task<ActionResult<ApiResponse<InviteResultDto>>> Invite(
            [FromBody] InviteRequest request,
            CancellationToken cancellationToken)
        {
            var senderId = GetUserId();

            if (string.Equals(senderId, request.TargetUserId, StringComparison.Ordinal))
                return BadRequest(ApiResponse<InviteResultDto>.ErrorResponse("You cannot invite yourself."));

            if (!DuelGames.TryGetValue(request.GameId ?? "", out var gameName))
                return BadRequest(ApiResponse<InviteResultDto>.ErrorResponse("That game cannot be played head to head."));

            var sender = await LoadNameAsync(senderId, cancellationToken);
            if (sender is null) return Unauthorized(ApiResponse<InviteResultDto>.ErrorResponse("Unknown sender."));

            var targetExists = await _context.Users
                .AnyAsync(u => u.Id == request.TargetUserId && u.IsActive, cancellationToken);

            if (!targetExists) return NotFound(ApiResponse<InviteResultDto>.ErrorResponse("That person is not available."));

            // The room both players will land in. Generated here rather than by whoever accepts
            // first, so the link in the notification is the room — there is no negotiation step
            // and no window where two people create two different rooms.
            var sessionId = Guid.NewGuid().ToString("N")[..10];
            var joinUrl = $"/minigames?duel={sessionId}&game={request.GameId}";

            await _notifications.CreateAsync(new CreateNotificationRequest
            {
                UserId = request.TargetUserId,
                MessageKey = NotificationKeys.MinigameInvite,
                Params = new Dictionary<string, string>
                {
                    ["fromName"] = sender,
                    ["gameName"] = gameName,
                },

                // No collapse key: two different people inviting you to two different games are
                // two separate things, and merging them would lose one of the links.
                DedupeKey = $"invite:{senderId}:{request.TargetUserId}:{sessionId}",
                WantsEmail = false,
                ActionUrl = joinUrl,
            }, cancellationToken);

            _logger.LogInformation(
                "Minigame invite from {Sender} to {Target} for {Game} ({Session}).",
                senderId, request.TargetUserId, request.GameId, sessionId);

            return Ok(ApiResponse<InviteResultDto>.SuccessResponse(
                new InviteResultDto { SessionId = sessionId, GameId = request.GameId!, JoinUrl = joinUrl },
                "Invite sent."));
        }

        private async Task<string?> LoadNameAsync(string userId, CancellationToken cancellationToken)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.FirstName, u.LastName })
                .FirstOrDefaultAsync(cancellationToken);

            if (user is null) return null;

            var name = $"{user.FirstName} {user.LastName}".Trim();
            return name.Length > 0 ? name : "A scholar";
        }

        public class InviteRequest
        {
            public string TargetUserId { get; set; } = string.Empty;
            public string? GameId { get; set; }
        }

        public class InviteResultDto
        {
            public string SessionId { get; set; } = string.Empty;
            public string GameId { get; set; } = string.Empty;

            /// <summary>Where the inviter should go to wait for the other player.</summary>
            public string JoinUrl { get; set; } = string.Empty;
        }

        public class DuelGameDto
        {
            public string GameId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }
    }
}
