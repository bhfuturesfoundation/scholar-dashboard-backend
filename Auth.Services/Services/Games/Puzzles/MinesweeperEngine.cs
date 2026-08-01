namespace Auth.Services.Services.Games.Puzzles
{
    /// <summary>
    /// Minesweeper, at the three standard board sizes.
    ///
    /// ── Why the mines are not placed until the first click ───────────────────
    ///
    /// The 1990 original placed them up front, so roughly one game in eight ended on move one
    /// through no fault of the player. Every serious implementation since has moved placement to
    /// after the opening click, and excludes that cell *and its neighbours* — which guarantees
    /// the first click opens a region rather than a single number, so there is always something
    /// to reason from. It is a small rule with a large effect: it converts the opening from a
    /// coin flip into a position.
    ///
    /// It also has a pleasant consequence here. The board is a pure function of (seed, first
    /// click), and the first click is the head of the move log — so a submitted game replays
    /// exactly, and the server can score it without having watched it.
    /// </summary>
    public static class MinesweeperEngine
    {
        public enum Level { Beginner = 0, Intermediate = 1, Expert = 2 }

        public sealed record BoardSpec(int Width, int Height, int Mines)
        {
            public int Cells => Width * Height;
        }

        /// <summary>The canonical dimensions. Changing these invalidates leaderboard comparisons.</summary>
        public static BoardSpec SpecFor(Level level) => level switch
        {
            Level.Beginner => new BoardSpec(9, 9, 10),
            Level.Intermediate => new BoardSpec(16, 16, 40),
            Level.Expert => new BoardSpec(30, 16, 99),
            _ => new BoardSpec(9, 9, 10),
        };

        public sealed record Board(BoardSpec Spec, bool[] Mines, int[] Adjacent);

        /// <summary>
        /// Lays mines uniformly, keeping the opening click and its eight neighbours clear.
        ///
        /// On Expert the safe patch is nine of 480 cells against 99 mines, so the exclusion is
        /// always satisfiable; the smaller boards have proportionally more room still.
        /// </summary>
        public static Board Generate(BoardSpec spec, int firstClick, ref DeterministicRng rng)
        {
            var safe = new HashSet<int>(Neighbours(spec, firstClick)) { firstClick };

            var candidates = new List<int>(spec.Cells);
            for (var i = 0; i < spec.Cells; i++)
            {
                if (!safe.Contains(i)) candidates.Add(i);
            }

            rng.Shuffle(candidates);

            var mines = new bool[spec.Cells];
            var toPlace = Math.Min(spec.Mines, candidates.Count);
            for (var i = 0; i < toPlace; i++) mines[candidates[i]] = true;

            var adjacent = new int[spec.Cells];
            for (var i = 0; i < spec.Cells; i++)
            {
                if (mines[i]) continue;

                var count = 0;
                foreach (var neighbour in Neighbours(spec, i))
                {
                    if (mines[neighbour]) count++;
                }

                adjacent[i] = count;
            }

            return new Board(spec, mines, adjacent);
        }

        public static IEnumerable<int> Neighbours(BoardSpec spec, int index)
        {
            var row = index / spec.Width;
            var column = index % spec.Width;

            for (var dr = -1; dr <= 1; dr++)
            {
                for (var dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;

                    var r = row + dr;
                    var c = column + dc;
                    if (r < 0 || r >= spec.Height || c < 0 || c >= spec.Width) continue;

                    yield return r * spec.Width + c;
                }
            }
        }

        /// <summary>
        /// Reveals a cell, cascading through the zero-adjacency region it belongs to.
        ///
        /// Iterative rather than recursive on purpose: an opening move on Expert can cascade
        /// through several hundred cells, and a recursive flood fill there is a stack overflow
        /// waiting for an unlucky board — one that would take down the whole process, not just
        /// the request.
        /// </summary>
        public static void Reveal(Board board, bool[] revealed, int index)
        {
            if (revealed[index]) return;

            var pending = new Stack<int>();
            pending.Push(index);

            while (pending.Count > 0)
            {
                var cell = pending.Pop();
                if (revealed[cell]) continue;

                revealed[cell] = true;

                // Cascade only from cells with no adjacent mines. A numbered cell is the edge of
                // the region and stops the fill — that boundary is the information the game is
                // played on.
                if (board.Adjacent[cell] != 0 || board.Mines[cell]) continue;

                foreach (var neighbour in Neighbours(board.Spec, cell))
                {
                    if (!revealed[neighbour]) pending.Push(neighbour);
                }
            }
        }

        public static bool IsCleared(Board board, bool[] revealed)
        {
            for (var i = 0; i < board.Spec.Cells; i++)
            {
                if (!board.Mines[i] && !revealed[i]) return false;
            }

            return true;
        }

        public sealed record ReplayResult(bool Valid, bool Cleared, int Revealed, string? Rejection);

        /// <summary>
        /// Re-runs a submitted game.
        ///
        /// Repeated reveals are skipped rather than rejected. The strict reading — an honest
        /// client never clicks a cell a cascade already opened — is true but not worth enforcing:
        /// a double tap on a phone would throw away a legitimately won Expert board, and unlike
        /// 2048 a padded log gains nothing here, because the mines are fixed by the first click
        /// and the score comes from the clock rather than the move count.
        /// </summary>
        public static ReplayResult Replay(uint seed, Level level, IReadOnlyList<int> reveals)
        {
            var spec = SpecFor(level);

            if (reveals.Count == 0)
                return new ReplayResult(false, false, 0, "Move log is empty.");

            // Four times the board rather than exactly the board. The tighter bound looks right
            // and quietly contradicts the leniency below: repeated reveals are tolerated, so an
            // honest log *can* be longer than the number of cells, and capping at the cell count
            // would throw away a won board because of a few double taps. This still bounds the
            // work, which is all the cap was ever for.
            if (reveals.Count > spec.Cells * 4)
                return new ReplayResult(false, false, 0, "Move log is longer than the board.");

            foreach (var cell in reveals)
            {
                if (cell < 0 || cell >= spec.Cells)
                    return new ReplayResult(false, false, 0, "Move log leaves the board.");
            }

            var rng = new DeterministicRng(seed);
            var board = Generate(spec, reveals[0], ref rng);
            var revealed = new bool[spec.Cells];

            foreach (var cell in reveals)
            {
                if (revealed[cell]) continue;

                // A mine cannot appear in an honest winning log — the client stops the game there.
                // Its presence means the log was assembled, so it is a rejection rather than a loss.
                if (board.Mines[cell])
                    return new ReplayResult(false, false, 0, "Move log reveals a mine.");

                Reveal(board, revealed, cell);
            }

            var count = 0;
            foreach (var wasRevealed in revealed)
            {
                if (wasRevealed) count++;
            }

            return new ReplayResult(true, IsCleared(board, revealed), count, null);
        }
    }
}
