namespace Auth.Models.Entities.Games
{
    /// <summary>
    /// One game in progress, so a refresh or a switch from laptop to phone does not lose it.
    ///
    /// ── Why the state is stored opaquely ─────────────────────────────────────
    ///
    /// <see cref="State"/> is JSON the server never parses. That looks careless and is the point:
    /// the save is not what the score is computed from. Scoring still happens by replaying the
    /// submitted move log against the signed seed, so a player who edits their save has not
    /// forged anything — they have produced a board the replay will disagree with, and the
    /// submission is rejected exactly as it would have been anyway.
    ///
    /// Keeping it opaque means each game owns its own save format, and adding a field to Tetris
    /// does not need a migration. The alternative — a modelled column per game per field — buys
    /// nothing, because there is no query that ever needs to look inside.
    ///
    /// One row per (user, game): resuming is the only use, so a history of abandoned boards would
    /// grow without bound and never be read.
    /// </summary>
    public class PuzzleSave
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        /// <summary>One of <see cref="PuzzleGames"/>.</summary>
        public string GameId { get; set; } = string.Empty;

        /// <summary>
        /// The signed ticket this game was dealt with.
        ///
        /// Stored because a resumed game has to submit the ticket it started from — a fresh one
        /// would carry a different seed and a later deal time, which is the difference between
        /// finishing a puzzle and claiming to have finished it in no time at all.
        /// </summary>
        public string Ticket { get; set; } = string.Empty;

        /// <summary>Client-defined JSON. See the note above on why this is not modelled.</summary>
        public string State { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
