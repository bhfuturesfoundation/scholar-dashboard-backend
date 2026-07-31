using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    public class GameScoreService : IGameScoreService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<GameScoreService> _logger;

        public GameScoreService(ApplicationDbContext context, ILogger<GameScoreService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// The legacy path: a score posted by the browser.
        ///
        /// Kept because the older minigames still use it, but everything written here is
        /// marked unverified, because it is — the client decided the number. The leaderboard
        /// filters these out by default rather than pretending they mean the same thing as a
        /// score the server computed.
        ///
        /// The cap is not security, it is damage control: it stops a single absurd value
        /// from permanently topping a board that people can still see.
        /// </summary>
        public async Task SubmitScoreAsync(string userId, string gameId, int score)
        {
            const int implausibleCeiling = 1_000_000;

            if (score < 0) score = 0;
            if (score > implausibleCeiling)
            {
                _logger.LogWarning(
                    "Clamped an implausible client-submitted score of {Score} for {Game} from {User}.",
                    score, gameId, userId);
                score = implausibleCeiling;
            }

            _context.GameScores.Add(new GameScore
            {
                Verified = false,
                UserId = userId,
                GameId = gameId,
                Score = score,
                PlayedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Score {Score} submitted for game {GameId} by user {UserId}", score, gameId, userId);
        }

        /// <summary>
        /// The leaderboard.
        ///
        /// <paramref name="verifiedOnly"/> defaults to true: a board that mixes scores the
        /// server computed with numbers a browser asserted is not a leaderboard, it is a
        /// suggestion. The unverified view is still reachable so the history of the older
        /// games is not simply hidden.
        /// </summary>
        public async Task<List<LeaderboardEntry>> GetLeaderboardAsync(
            string gameId, int top = 10, bool verifiedOnly = true)
        {
            // Step 1: get each user's personal best for this game, ranked
            var bestScores = await _context.GameScores
                .Where(g => g.GameId == gameId && (!verifiedOnly || g.Verified))
                .GroupBy(g => g.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    BestScore = g.Max(x => x.Score),
                    LastPlayed = g.Max(x => x.PlayedAt)
                })
                .OrderByDescending(g => g.BestScore)
                .Take(top)
                .ToListAsync();

            if (bestScores.Count == 0)
                return new List<LeaderboardEntry>();

            // Step 2: fetch display names for those users only
            var userIds = bestScores.Select(b => b.UserId).ToList();
            var users = await _context.Users
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FirstName, u.LastName })
                .ToListAsync();

            var userMap = users.ToDictionary(u => u.Id);

            return bestScores
                .Select((entry, index) =>
                {
                    var user = userMap.GetValueOrDefault(entry.UserId);
                    // Show "John D." — first name + last initial for a hint of privacy
                    var displayName = user != null
                        ? $"{user.FirstName} {(user.LastName?.Length > 0 ? user.LastName[0] + "." : string.Empty)}"
                        : "Unknown";
                    return new LeaderboardEntry(index + 1, displayName.Trim(), entry.BestScore, entry.LastPlayed);
                })
                .ToList();
        }

        public async Task<List<PersonalBest>> GetPersonalBestsAsync(string userId)
        {
            return await _context.GameScores
                .Where(g => g.UserId == userId)
                .GroupBy(g => g.GameId)
                .Select(g => new PersonalBest(
                    g.Key,
                    g.Max(x => x.Score),
                    g.Max(x => x.PlayedAt)))
                .ToListAsync();
        }
    }
}
