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

        /// <summary>
        /// True only when the server computed this score itself.
        ///
        /// Everything written before Comet Arena came from `SubmitScoreAsync(user, game,
        /// score)` — a number posted by the browser, which anyone with a console could set
        /// to whatever they liked. Those rows are kept rather than deleted, because they are
        /// a real record of people playing, but the leaderboard defaults to verified only.
        ///
        /// The distinction is not a validation flag. A verified score was never in the
        /// client's hands to begin with: the server owned the simulation and did the
        /// arithmetic, so there was nothing to forge.
        /// </summary>
        public bool Verified { get; set; }

        /// <summary>The match this came from. Null for client-submitted scores.</summary>
        public string? SessionId { get; set; }

        /// <summary>ArenaMode as an int; null for games that have no modes.</summary>
        public int? Mode { get; set; }

        public int? DurationSeconds { get; set; }

        /// <summary>Highest combo reached. Shown on the leaderboard as a quality signal.</summary>
        public int? BestCombo { get; set; }
    }
}
