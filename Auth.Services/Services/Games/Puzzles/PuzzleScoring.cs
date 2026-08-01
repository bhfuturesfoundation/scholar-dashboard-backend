namespace Auth.Services.Services.Games.Puzzles
{
    /// <summary>
    /// Turns a finishing time into a leaderboard number.
    ///
    /// ── Why a curve rather than a subtraction ────────────────────────────────
    ///
    /// Sudoku and Minesweeper are timed games, but the leaderboard sorts descending, so time has
    /// to be inverted. The obvious `par - seconds` is wrong in both directions: it goes negative
    /// for anyone slower than par — which reads as a punishment for finishing — and it is linear,
    /// so shaving ten seconds off a four-minute solve is worth exactly as much as shaving ten off
    /// a forty-second one, when the first is ordinary and the second is remarkable.
    ///
    ///     score = base × 2·par / (par + seconds)
    ///
    /// A finish at par scores base. Faster approaches 2× base and never exceeds it, so there is
    /// no reward for a suspiciously instant time. Slower decays smoothly toward zero without ever
    /// reaching it, so a slow solve still counts for something. And the gradient is steepest near
    /// par, which is where players actually compete.
    ///
    /// ── The limit of this ────────────────────────────────────────────────────
    ///
    /// Replay verification proves a submitted game was really played on the board the server
    /// dealt. It cannot prove a human played it — a solver script would produce a perfectly valid
    /// log. <see cref="MinimumPlausibleSeconds"/> rules out the crude version of that, and the
    /// 2× ceiling caps what it is worth. Beyond that this is a scholarship platform's games
    /// section, and the honest position is that the leaderboard is trustworthy against a browser
    /// console, not against a determined script.
    /// </summary>
    public static class PuzzleScoring
    {
        public sealed record Curve(int BasePoints, int ParSeconds, int MinimumPlausibleSeconds);

        /// <summary>
        /// Par times are set around a competent-but-unhurried finish, not a record. The floors
        /// are set below any plausible human time rather than near it — the intent is to reject
        /// scripts, and a floor tight enough to catch a fast one would also reject a good player
        /// having a good day, which is much the worse error.
        /// </summary>
        public static Curve ForSudoku(SudokuEngine.Level level) => level switch
        {
            SudokuEngine.Level.Easy => new Curve(1000, 300, 45),
            SudokuEngine.Level.Medium => new Curve(2000, 480, 70),
            SudokuEngine.Level.Hard => new Curve(3500, 700, 90),
            SudokuEngine.Level.Expert => new Curve(5000, 900, 110),
            _ => new Curve(1000, 300, 45),
        };

        public static Curve ForMinesweeper(MinesweeperEngine.Level level) => level switch
        {
            MinesweeperEngine.Level.Beginner => new Curve(1000, 40, 4),
            MinesweeperEngine.Level.Intermediate => new Curve(2500, 120, 15),
            MinesweeperEngine.Level.Expert => new Curve(5000, 250, 35),
            _ => new Curve(1000, 40, 4),
        };

        /// <summary>
        /// Sokoban is scored on moves, not the clock — see SokobanEngine. The curve is the same
        /// shape, so a solution at par pays base and a shorter one pays up to double.
        /// </summary>
        public static Curve ForSokoban(int parMoves) => new(600, Math.Max(2, parMoves), 0);

        public static int FromTime(Curve curve, int seconds)
        {
            var elapsed = Math.Max(0, seconds);
            var value = curve.BasePoints * (2.0 * curve.ParSeconds) / (curve.ParSeconds + elapsed);
            return (int)Math.Round(value);
        }

        /// <summary>Each hint costs a sixth of the score, down to a floor of a quarter.</summary>
        public static int ApplyHintPenalty(int score, int hintsUsed)
        {
            if (hintsUsed <= 0) return score;

            var retained = Math.Max(0.25, 1.0 - 0.1667 * hintsUsed);
            return (int)Math.Round(score * retained);
        }
    }
}
