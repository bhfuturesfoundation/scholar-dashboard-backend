namespace Auth.Models.Response
{
    public record LeaderboardEntry(
        int Rank,
        string DisplayName,
        int Score,
        DateTime PlayedAt
    );

    public record PersonalBest(
        string GameId,
        int BestScore,
        DateTime LastPlayedAt
    );
}
