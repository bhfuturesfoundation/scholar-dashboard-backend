namespace Auth.Models.Entities
{
    public class GameScore
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        /// <summary>
        /// Matches the gameId strings used in the frontend (e.g. "tap-sprint", "comet-dodge").
        /// </summary>
        public string GameId { get; set; } = string.Empty;

        public int Score { get; set; }
        public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    }
}
