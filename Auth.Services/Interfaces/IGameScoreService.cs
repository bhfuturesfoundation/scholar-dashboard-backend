using Auth.Models.Response;

namespace Auth.Services.Interfaces
{
    public interface IGameScoreService
    {
        Task SubmitScoreAsync(string userId, string gameId, int score);
        Task<List<LeaderboardEntry>> GetLeaderboardAsync(string gameId, int top = 10);
        Task<List<PersonalBest>> GetPersonalBestsAsync(string userId);
    }
}
