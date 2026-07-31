using Auth.Models.Response;

namespace Auth.Services.Interfaces
{
    public interface IGameScoreService
    {
        Task SubmitScoreAsync(string userId, string gameId, int score);
        /// <summary>
        /// The leaderboard. Verified-only by default — see GameScore.Verified for why a
        /// board that mixes server-computed scores with client-asserted ones is not a
        /// leaderboard.
        /// </summary>
        Task<List<LeaderboardEntry>> GetLeaderboardAsync(string gameId, int top = 10, bool verifiedOnly = true);
        Task<List<PersonalBest>> GetPersonalBestsAsync(string userId);
    }
}
