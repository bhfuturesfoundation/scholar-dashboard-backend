namespace Auth.Services.Services.Games.Puzzles
{
    /// <summary>
    /// 2048, implemented to Gabriele Cirulli's original rules.
    ///
    /// Deliberately not reinvented. The reason this game has a twelve-year skill ceiling is that
    /// its rules were tuned until they were exactly right — spawn a 4 ten percent of the time
    /// rather than five or twenty, forbid a tile from merging twice in one move, spawn only after
    /// a move that changed something. Each of those is the difference between a game people study
    /// and a game people close. So the numbers below are the published ones, and the tests pin
    /// them.
    ///
    /// ── Why the server can score this ────────────────────────────────────────
    ///
    /// Every tile after the first two is placed by <see cref="DeterministicRng"/>, and the
    /// generator only advances on moves that changed the board. So the entire game is a pure
    /// function of (seed, move list): replay the moves, get the same boards, get the same score.
    /// The client is free to run its own copy for instant feedback, but the number that reaches
    /// the leaderboard is the one this file computed.
    /// </summary>
    public static class Game2048Engine
    {
        public const int Size = 4;
        public const int Cells = Size * Size;

        /// <summary>The published spawn distribution: a 4 one time in ten, otherwise a 2.</summary>
        private const double FourSpawnChance = 0.1;

        public enum Direction { Up = 0, Right = 1, Down = 2, Left = 3 }

        public sealed record MoveOutcome(bool Changed, int ScoreGained);

        /// <summary>The opening board: two tiles, same as a fresh game anywhere else.</summary>
        public static int[] Deal(ref DeterministicRng rng)
        {
            var board = new int[Cells];
            SpawnTile(board, ref rng);
            SpawnTile(board, ref rng);
            return board;
        }

        /// <summary>
        /// Applies one move in place and reports whether anything moved.
        ///
        /// The caller needs that flag rather than just the score, because a move that changes
        /// nothing must not spawn a tile. Getting this wrong is the classic 2048 bug: pressing a
        /// direction that is already flush against the wall would keep filling the board, and the
        /// game becomes unwinnable through no fault of the player.
        /// </summary>
        public static MoveOutcome ApplyMove(int[] board, Direction direction)
        {
            var changed = false;
            var gained = 0;

            for (var line = 0; line < Size; line++)
            {
                var indices = LineIndices(direction, line);

                var values = new int[Size];
                for (var i = 0; i < Size; i++) values[i] = board[indices[i]];

                var (collapsed, lineScore) = Collapse(values);
                gained += lineScore;

                for (var i = 0; i < Size; i++)
                {
                    if (board[indices[i]] == collapsed[i]) continue;

                    board[indices[i]] = collapsed[i];
                    changed = true;
                }
            }

            return new MoveOutcome(changed, gained);
        }

        /// <summary>
        /// Slides one line toward index 0 and merges equal neighbours.
        ///
        /// `lastMergedInto` is what stops a tile merging twice in a single move: without it a row
        /// of 2 2 4 would collapse to a single 8, and the whole risk/reward shape of the game
        /// changes — chaining becomes free and the board stops filling up.
        /// </summary>
        private static (int[] Line, int Score) Collapse(int[] values)
        {
            var result = new int[Size];
            var write = 0;
            var score = 0;
            var lastMergedInto = -1;

            foreach (var value in values)
            {
                if (value == 0) continue;

                if (write > 0 && result[write - 1] == value && lastMergedInto != write - 1)
                {
                    result[write - 1] = value * 2;
                    score += value * 2;
                    lastMergedInto = write - 1;
                    continue;
                }

                result[write++] = value;
            }

            return (result, score);
        }

        /// <summary>
        /// Board indices for one row or column, ordered so that index 0 is the wall the tiles
        /// slide into. That ordering is the only thing that differs between the four directions,
        /// which is why <see cref="Collapse"/> can stay direction-agnostic.
        /// </summary>
        private static int[] LineIndices(Direction direction, int line)
        {
            var indices = new int[Size];

            for (var i = 0; i < Size; i++)
            {
                indices[i] = direction switch
                {
                    Direction.Left => line * Size + i,
                    Direction.Right => line * Size + (Size - 1 - i),
                    Direction.Up => i * Size + line,
                    Direction.Down => (Size - 1 - i) * Size + line,
                    _ => throw new ArgumentOutOfRangeException(nameof(direction)),
                };
            }

            return indices;
        }

        /// <summary>Places a tile in a uniformly chosen empty cell. No-op on a full board.</summary>
        public static void SpawnTile(int[] board, ref DeterministicRng rng)
        {
            var empty = new List<int>(Cells);
            for (var i = 0; i < Cells; i++)
            {
                if (board[i] == 0) empty.Add(i);
            }

            if (empty.Count == 0) return;

            // Value drawn before the position, and both always drawn together. Any replay has to
            // consume the generator in exactly this order or every later board diverges.
            var value = rng.NextDouble() < FourSpawnChance ? 4 : 2;
            board[empty[rng.Next(empty.Count)]] = value;
        }

        /// <summary>Over when the board is full and no two neighbours match.</summary>
        public static bool IsGameOver(int[] board)
        {
            for (var i = 0; i < Cells; i++)
            {
                if (board[i] == 0) return false;

                var row = i / Size;
                var column = i % Size;

                if (column + 1 < Size && board[i] == board[i + 1]) return false;
                if (row + 1 < Size && board[i] == board[i + Size]) return false;
            }

            return true;
        }

        public static int HighestTile(int[] board)
        {
            var highest = 0;
            foreach (var value in board)
            {
                if (value > highest) highest = value;
            }
            return highest;
        }

        public sealed record ReplayResult(bool Valid, int Score, int HighestTile, int MovesPlayed, string? Rejection);

        /// <summary>
        /// Re-runs a submitted game and returns the score the server computed.
        ///
        /// Moves that would not change the board are a rejection rather than a skip. They cannot
        /// happen in honest play — the client never sends a move it did not apply — so their
        /// presence means the log was assembled rather than played, and silently ignoring them
        /// would let a forger pad a log until the spawn sequence produced a board it liked.
        /// </summary>
        public static ReplayResult Replay(uint seed, IReadOnlyList<int> moves, int maxMoves)
        {
            if (moves.Count > maxMoves)
                return new ReplayResult(false, 0, 0, 0, "Move log is too long.");

            var rng = new DeterministicRng(seed);
            var board = Deal(ref rng);
            var score = 0;

            for (var i = 0; i < moves.Count; i++)
            {
                if (moves[i] is < 0 or > 3)
                    return new ReplayResult(false, 0, 0, i, "Move log contains an unknown direction.");

                if (IsGameOver(board))
                    return new ReplayResult(false, 0, 0, i, "Move log continues past the end of the game.");

                var outcome = ApplyMove(board, (Direction)moves[i]);
                if (!outcome.Changed)
                    return new ReplayResult(false, 0, 0, i, "Move log contains a move that changes nothing.");

                score += outcome.ScoreGained;
                SpawnTile(board, ref rng);
            }

            return new ReplayResult(true, score, HighestTile(board), moves.Count, null);
        }
    }
}
