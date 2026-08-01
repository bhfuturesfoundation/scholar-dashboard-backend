namespace Auth.Services.Services.Games.Puzzles
{
    /// <summary>
    /// Tetris, scored to the modern guideline.
    ///
    /// ── What the client sends, and why it is not the inputs ──────────────────
    ///
    /// The obvious way to verify a real-time game is to record every keypress with a frame number
    /// and re-run the simulation. It works, and it is the wrong tool here: it makes the score
    /// depend on gravity timing, lock delay and input latency, so a dropped frame on a phone
    /// becomes a desync and an honest player loses their game to a stutter.
    ///
    /// What actually determines a Tetris score is *where each piece came to rest* — line clears,
    /// combos and back-to-backs all follow from the board. So a submission is a list of
    /// placements, and the server checks each one is legal on the board as it stands: in bounds,
    /// not overlapping, and resting on something. Timing never enters into it.
    ///
    /// The anti-forgery property comes from the bag. The piece for placement N is fixed by the
    /// signed seed, so the client chooses *where* a piece goes but never *which* piece it gets —
    /// which is exactly the constraint the game is built on.
    ///
    /// The known gap: "resting on something" admits placements a human could not physically
    /// slide into under an overhang. Verifying reachability means pathfinding per piece, and it
    /// would still not distinguish a good player from a script. It is left out deliberately, on
    /// the same reasoning as the other puzzles — this stops invented scores, not solvers.
    /// </summary>
    public static class TetrisEngine
    {
        public const int Width = 10;
        public const int Height = 20;
        public const int Cells = Width * Height;

        /// <summary>Roughly ten times the longest human marathon. A bound on replay cost.</summary>
        public const int MaxPlacements = 20_000;

        public enum Piece { I = 0, J = 1, L = 2, O = 3, S = 4, T = 5, Z = 6 }

        /// <summary>
        /// Cell offsets for each piece and rotation, in the Super Rotation System's orientations.
        ///
        /// Written out as explicit offsets rather than derived by rotating a matrix. Matrix
        /// rotation looks tidier and gets the pivots subtly wrong — SRS does not rotate about the
        /// centre of the bounding box for I and O — and "subtly wrong rotation" is the kind of
        /// bug that only shows up as a piece that will not fit a gap it obviously should.
        /// </summary>
        private static readonly int[][][] Shapes =
        {
            // I
            new[] { new[] { 0, 1, 1, 1, 2, 1, 3, 1 }, new[] { 2, 0, 2, 1, 2, 2, 2, 3 },
                    new[] { 0, 2, 1, 2, 2, 2, 3, 2 }, new[] { 1, 0, 1, 1, 1, 2, 1, 3 } },
            // J
            new[] { new[] { 0, 0, 0, 1, 1, 1, 2, 1 }, new[] { 1, 0, 2, 0, 1, 1, 1, 2 },
                    new[] { 0, 1, 1, 1, 2, 1, 2, 2 }, new[] { 1, 0, 1, 1, 0, 2, 1, 2 } },
            // L
            new[] { new[] { 2, 0, 0, 1, 1, 1, 2, 1 }, new[] { 1, 0, 1, 1, 1, 2, 2, 2 },
                    new[] { 0, 1, 1, 1, 2, 1, 0, 2 }, new[] { 0, 0, 1, 0, 1, 1, 1, 2 } },
            // O
            new[] { new[] { 0, 0, 1, 0, 0, 1, 1, 1 }, new[] { 0, 0, 1, 0, 0, 1, 1, 1 },
                    new[] { 0, 0, 1, 0, 0, 1, 1, 1 }, new[] { 0, 0, 1, 0, 0, 1, 1, 1 } },
            // S
            new[] { new[] { 1, 0, 2, 0, 0, 1, 1, 1 }, new[] { 1, 0, 1, 1, 2, 1, 2, 2 },
                    new[] { 1, 1, 2, 1, 0, 2, 1, 2 }, new[] { 0, 0, 0, 1, 1, 1, 1, 2 } },
            // T
            new[] { new[] { 1, 0, 0, 1, 1, 1, 2, 1 }, new[] { 1, 0, 1, 1, 2, 1, 1, 2 },
                    new[] { 0, 1, 1, 1, 2, 1, 1, 2 }, new[] { 1, 0, 0, 1, 1, 1, 1, 2 } },
            // Z
            new[] { new[] { 0, 0, 1, 0, 1, 1, 2, 1 }, new[] { 2, 0, 1, 1, 2, 1, 1, 2 },
                    new[] { 0, 1, 1, 1, 1, 2, 2, 2 }, new[] { 1, 0, 0, 1, 1, 1, 0, 2 } },
        };

        /// <summary>The four (x, y) cells a piece occupies at a given rotation and position.</summary>
        public static (int X, int Y)[] CellsOf(Piece piece, int rotation, int x, int y)
        {
            var shape = Shapes[(int)piece][((rotation % 4) + 4) % 4];

            return new[]
            {
                (x + shape[0], y + shape[1]),
                (x + shape[2], y + shape[3]),
                (x + shape[4], y + shape[5]),
                (x + shape[6], y + shape[7]),
            };
        }

        /// <summary>
        /// The 7-bag randomiser.
        ///
        /// Every seven pieces contain each tetromino exactly once. Uniform random selection is
        /// the naive alternative and it is a materially worse game: it can withhold an I-piece
        /// for thirty placements, so a well-built board dies to variance rather than to a
        /// mistake. The bag is why modern Tetris rewards planning.
        /// </summary>
        public static Piece[] Bag(ref DeterministicRng rng)
        {
            var bag = new List<int> { 0, 1, 2, 3, 4, 5, 6 };
            rng.Shuffle(bag);
            return bag.ConvertAll(value => (Piece)value).ToArray();
        }

        /// <summary>One placement: where a piece came to rest, and whether hold was used first.</summary>
        public sealed record Placement(int Rotation, int X, int Y, bool UsedHold);

        public sealed record ReplayResult(
            bool Valid, int Score, int Lines, int Level, int Tetrises, int BestCombo, string? Rejection);

        /// <summary>Guideline line-clear values, before the level multiplier.</summary>
        private static int ClearValue(int lines) => lines switch
        {
            1 => 100,
            2 => 300,
            3 => 500,
            4 => 800,
            _ => 0,
        };

        public static ReplayResult Replay(uint seed, IReadOnlyList<Placement> placements)
        {
            if (placements.Count > MaxPlacements)
                return Fail("Move log is too long.");

            var rng = new DeterministicRng(seed);
            var board = new bool[Cells];

            var queue = new Queue<Piece>(Bag(ref rng));
            Piece? held = null;

            var score = 0;
            var lines = 0;
            var tetrises = 0;
            var combo = -1;
            var bestCombo = 0;
            var backToBack = false;

            for (var i = 0; i < placements.Count; i++)
            {
                if (queue.Count <= 7) foreach (var piece in Bag(ref rng)) queue.Enqueue(piece);

                var placement = placements[i];
                var current = queue.Dequeue();

                if (placement.UsedHold)
                {
                    if (held is null)
                    {
                        // First hold of the game: the held piece becomes current, and the next
                        // one is drawn. Same rule the client follows.
                        held = current;
                        if (queue.Count == 0) foreach (var piece in Bag(ref rng)) queue.Enqueue(piece);
                        current = queue.Dequeue();
                    }
                    else
                    {
                        (current, held) = (held.Value, current);
                    }
                }

                if (placement.Rotation is < 0 or > 3)
                    return Fail($"Placement {i} has an unknown rotation.");

                var cells = CellsOf(current, placement.Rotation, placement.X, placement.Y);

                foreach (var (x, y) in cells)
                {
                    if (x < 0 || x >= Width || y < 0 || y >= Height)
                        return Fail($"Placement {i} is off the board.");

                    if (board[y * Width + x])
                        return Fail($"Placement {i} overlaps a settled piece.");
                }

                // Resting on something: at least one cell has floor or a settled cell directly
                // below, and no cell of the piece itself. A piece floating in mid-air is the
                // clearest signal a log was assembled rather than played.
                var supported = false;
                foreach (var (x, y) in cells)
                {
                    if (y + 1 >= Height) { supported = true; break; }

                    var below = (y + 1) * Width + x;
                    if (!board[below]) continue;
                    supported = true;
                    break;
                }

                if (!supported)
                    return Fail($"Placement {i} does not rest on anything.");

                foreach (var (x, y) in cells) board[y * Width + x] = true;

                var cleared = ClearLines(board);

                if (cleared == 0)
                {
                    combo = -1;
                    continue;
                }

                lines += cleared;
                if (cleared == 4) tetrises++;

                combo++;
                if (combo > bestCombo) bestCombo = combo;

                var level = lines / 10 + 1;
                var value = ClearValue(cleared) * level;

                // Back-to-back: consecutive Tetrises are worth half again. It is the reason good
                // players build a well and wait rather than clearing whatever is available.
                var difficult = cleared == 4;
                if (difficult && backToBack) value = value * 3 / 2;
                backToBack = difficult;

                if (combo > 0) value += 50 * combo * level;

                // Perfect clear: the board is empty. Rare enough to be worth calling out.
                if (Array.TrueForAll(board, cell => !cell)) value += 1000 * level;

                score += value;
            }

            return new ReplayResult(true, score, lines, lines / 10 + 1, tetrises, bestCombo, null);

            static ReplayResult Fail(string reason) => new(false, 0, 0, 0, 0, 0, reason);
        }

        /// <summary>Removes full rows and drops everything above them down. Returns how many went.</summary>
        public static int ClearLines(bool[] board)
        {
            var cleared = 0;

            // Bottom upwards, and the write cursor only moves when a row survives — so rows fall
            // by however many were removed beneath them, in one pass.
            var write = Height - 1;

            for (var read = Height - 1; read >= 0; read--)
            {
                var full = true;
                for (var x = 0; x < Width; x++)
                {
                    if (board[read * Width + x]) continue;
                    full = false;
                    break;
                }

                if (full)
                {
                    cleared++;
                    continue;
                }

                if (write != read)
                {
                    for (var x = 0; x < Width; x++) board[write * Width + x] = board[read * Width + x];
                }

                write--;
            }

            for (; write >= 0; write--)
            {
                for (var x = 0; x < Width; x++) board[write * Width + x] = false;
            }

            return cleared;
        }
    }
}
