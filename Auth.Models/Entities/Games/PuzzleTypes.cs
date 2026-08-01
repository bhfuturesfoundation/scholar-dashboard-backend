namespace Auth.Models.Entities.Games
{
    /// <summary>The game ids these puzzles record scores under. Shared with the frontend library.</summary>
    public static class PuzzleGames
    {
        public const string Sudoku = "sudoku";
        public const string Game2048 = "tile-2048";
        public const string Minesweeper = "minesweeper";
        public const string Tetris = "tetris";
        public const string Sokoban = "sokoban";

        public static bool IsKnown(string? gameId) =>
            gameId is Sudoku or Game2048 or Minesweeper or Tetris or Sokoban;
    }

    /// <summary>
    /// A freshly dealt puzzle.
    ///
    /// <see cref="Ticket"/> is opaque to the client and must be handed back on submission — it
    /// carries the signed seed and deal time the score is computed from. Note what is *not* here:
    /// the Sudoku solution, and the mine positions. Both stay on the server, because a client
    /// that can see them is playing a different game.
    /// </summary>
    public class PuzzleDeal
    {
        public string GameId { get; set; } = string.Empty;
        public int Difficulty { get; set; }
        public string Ticket { get; set; } = string.Empty;

        /// <summary>Sudoku only: 81 cells, 0 for blank.</summary>
        public int[]? Givens { get; set; }

        /// <summary>2048 only: the opening 16-cell board.</summary>
        public int[]? Board { get; set; }

        /// <summary>
        /// Tetris only: the seed the piece bags are drawn from.
        ///
        /// Deliberately handed over. Tetris is the one game here where the client must agree with
        /// the server on the piece sequence — the replay checks each placement against the piece
        /// *its* bag dealt — so a client with a different bag would have every honest game
        /// rejected. Knowing the sequence ahead is a modest advantage; it is not the ability to
        /// invent a score, which is what the signature and the replay protect.
        /// </summary>
        public uint? Seed { get; set; }

        /// <summary>Sokoban only: the level grid, in standard notation.</summary>
        public string[]? Rows { get; set; }

        /// <summary>Sokoban only: the move count a solid solution takes.</summary>
        public int? Par { get; set; }

        /// <summary>Sokoban only, for the level picker.</summary>
        public string? LevelName { get; set; }
        public int? LevelCount { get; set; }

        /// <summary>Minesweeper only.</summary>
        public int Width { get; set; }
        public int Height { get; set; }
        public int Mines { get; set; }
    }

    /// <summary>
    /// The Minesweeper board, sent only once the opening click has been made.
    ///
    /// ── Why the mines are handed over at all ─────────────────────────────────
    ///
    /// They have to be. The client cannot draw a single number without them, and the alternative
    /// — a request per click so the server reveals cells one at a time — would put a round trip
    /// between a player and every square they open. On Expert that is a few hundred of them, and
    /// the game is unplayable at 80ms a click.
    ///
    /// What this costs is worth stating plainly: someone who reads this response can see the
    /// mines, and could script a perfect clear. What it does *not* cost is the thing that
    /// actually matters — a score still has to be a real playthrough of the board the server
    /// generated from a seed it chose and signed, so scores cannot be invented, only played
    /// unusually well. <c>MinimumPlausibleSeconds</c> catches the crude version of that.
    ///
    /// The board is withheld until the first click for a second reason besides the safe-opening
    /// rule: it means a player cannot see anything before committing to a square.
    /// </summary>
    public class MinesweeperBoard
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public bool[] Mines { get; set; } = Array.Empty<bool>();
        public int[] Adjacent { get; set; } = Array.Empty<int>();
    }

    public class SudokuHint
    {
        public int Cell { get; set; }
        public int Digit { get; set; }
    }

    public class PuzzleSubmission
    {
        public string Ticket { get; set; } = string.Empty;

        /// <summary>Sudoku: the completed 81-cell grid.</summary>
        public int[]? Grid { get; set; }

        /// <summary>2048: directions played. Minesweeper: cells revealed, in order.</summary>
        public int[]? Moves { get; set; }

        /// <summary>Sudoku only. Each one costs a slice of the final score.</summary>
        public int HintsUsed { get; set; }

        /// <summary>
        /// Tetris: where each piece came to rest. Not the keypresses — see TetrisEngine for why
        /// timing is deliberately kept out of the scored record.
        /// </summary>
        public TetrisPlacementDto[]? Placements { get; set; }
    }

    public class TetrisPlacementDto
    {
        public int Rotation { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool UsedHold { get; set; }
    }

    public class PuzzleSaveDto
    {
        public string Ticket { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }

        /// <summary>Seconds since the game was dealt, so the client can show what it is resuming.</summary>
        public int AgeSeconds { get; set; }
    }

    public class PuzzleOutcome
    {
        public bool Accepted { get; set; }

        /// <summary>Why the submission was not scored. Null when it was.</summary>
        public string? Reason { get; set; }

        /// <summary>The number the server computed. Never one the client sent.</summary>
        public int Score { get; set; }

        public int Seconds { get; set; }
        public bool PersonalBest { get; set; }

        /// <summary>2048 only: the largest tile reached.</summary>
        public int? HighestTile { get; set; }

        /// <summary>Sokoban only.</summary>
        public int? Moves { get; set; }

        /// <summary>Tetris only.</summary>
        public int? Lines { get; set; }
        public int? Level { get; set; }
        public int? Tetrises { get; set; }
    }
}
