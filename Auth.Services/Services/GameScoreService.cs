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

        public async Task SubmitScoreAsync(string userId, string gameId, int score)
        {
            _context.GameScores.Add(new GameScore
            {
                UserId = userId,
                GameId = gameId,
                Score = score,
                PlayedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            _logger.LogInformation("Score {Score} submitted for game {GameId} by user {UserId}", score, gameId, userId);
        }

        public async Task<List<LeaderboardEntry>> GetLeaderboardAsync(string gameId, int top = 10)
        {
            // Step 1: get each user's personal best for this game, ranked
            var bestScores = await _context.GameScores
                .Where(g => g.GameId == gameId)
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
