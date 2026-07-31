namespace Auth.Models.Entities.Games
{
    /// <summary>
    /// The world state of one Comet Arena match.
    ///
    /// WHY THIS LIVES ON THE SERVER
    /// ----------------------------
    /// The old leaderboard took a number from the browser: `SubmitScoreAsync(user, game,
    /// score)`. No amount of validation makes that trustworthy — the client is the thing
    /// being asked, so anyone with a console can type any score. A leaderboard built on it
    /// is decoration.
    ///
    /// The only fix is to stop the client computing a score at all. The server owns every
    /// position, every collision and every point; the client sends a direction and draws
    /// what it is told. Then a score is not "validated", it is simply the server's own
    /// arithmetic, and there is nothing to forge.
    ///
    /// This also makes co-op and versus fall out for free: a server already simulating a
    /// shared world does not care how many players are in it.
    ///
    /// The state is deliberately plain data with no behaviour, so the tick function can be
    /// a pure transformation over it and therefore unit-testable without a hub, a socket or
    /// a database.
    /// </summary>
    public sealed class ArenaState
    {
        public string SessionId { get; set; } = string.Empty;
        public ArenaMode Mode { get; set; }
        public ArenaPhase Phase { get; set; } = ArenaPhase.Lobby;

        public int Tick { get; set; }

        /// <summary>
        /// The RNG cursor.
        ///
        /// A plain integer advanced by a small hash rather than System.Random, so a match
        /// is fully reproducible from its seed. That is what makes a disputed score
        /// re-runnable, and it costs nothing.
        /// </summary>
        public uint RandomState { get; set; }

        public List<ArenaPlayer> Players { get; set; } = new();
        public List<ArenaOrb> Orbs { get; set; } = new();
        public List<ArenaComet> Comets { get; set; } = new();

        public DateTime StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
    }

    public enum ArenaMode
    {
        /// <summary>One player, score attack. This is what feeds the global leaderboard.</summary>
        Solo = 0,

        /// <summary>Shared score. Everyone's collection adds to one total.</summary>
        Coop = 1,

        /// <summary>Highest individual score wins, and players can shove each other.</summary>
        Versus = 2
    }

    public enum ArenaPhase
    {
        Lobby = 0,
        Countdown = 1,
        Running = 2,
        Finished = 3
    }

    public sealed class ArenaPlayer
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Index into the client's palette, assigned on join.</summary>
        public int ColorIndex { get; set; }

        public float X { get; set; }
        public float Y { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }

        /// <summary>
        /// Latest input direction, already normalised by the server.
        ///
        /// Normalising here rather than trusting the client is the small but essential
        /// check: a client sending (100, 100) instead of a unit vector would otherwise move
        /// 140× faster than everyone else, which is the cheapest possible cheat.
        /// </summary>
        public float InputX { get; set; }
        public float InputY { get; set; }

        /// <summary>Ticks remaining before this player can dash again.</summary>
        public int DashCooldown { get; set; }

        /// <summary>Ticks remaining stunned after a comet hit. Cannot move or collect.</summary>
        public int StunTicks { get; set; }

        public int Score { get; set; }

        /// <summary>
        /// Consecutive orbs collected without being hit. Drives the multiplier, and is the
        /// reason the game has a skill ceiling rather than being a collection rate contest.
        /// </summary>
        public int Combo { get; set; }

        public int BestCombo { get; set; }
        public int OrbsCollected { get; set; }
        public int CometHits { get; set; }

        /// <summary>False once they disconnect; they stay in the state so the final score is recorded.</summary>
        public bool Connected { get; set; } = true;
    }

    public sealed class ArenaOrb
    {
        public int Id { get; set; }
        public float X { get; set; }
        public float Y { get; set; }

        /// <summary>Higher-value orbs spawn nearer the edge, where the comets are.</summary>
        public int Value { get; set; }
    }

    public sealed class ArenaComet
    {
        public int Id { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public float Radius { get; set; }
    }
}
