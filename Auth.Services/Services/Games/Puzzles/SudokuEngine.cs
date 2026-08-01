namespace Auth.Services.Services.Games.Puzzles
{
    /// <summary>
    /// Generates and checks Sudoku puzzles.
    ///
    /// ── The property that makes a grid a puzzle ──────────────────────────────
    ///
    /// Removing clues from a solved grid until it looks sparse produces something that resembles
    /// a Sudoku and is not one. The rule that matters is that exactly one completion exists: a
    /// grid with two solutions cannot be reasoned to an answer, only guessed at, and a solver who
    /// deduces correctly still has a fifty percent chance of being told they are wrong. That is
    /// the single worst experience a puzzle game can deliver, and it is invisible unless it is
    /// checked for.
    ///
    /// So every clue removed here is provisional: it comes out, the remainder is solved twice
    /// over, and it goes straight back if a second solution turns up. The cost is roughly eighty
    /// backtracking solves per puzzle — a few milliseconds — and it is what separates this from
    /// the naive version.
    /// </summary>
    public static class SudokuEngine
    {
        public const int Size = 9;
        public const int Cells = Size * Size;

        public enum Level { Easy = 0, Medium = 1, Hard = 2, Expert = 3 }

        /// <summary>
        /// How many clues to aim for.
        ///
        /// A target, not a guarantee. Digging stops early when no further clue can be removed
        /// without admitting a second solution, so a puzzle may be handed out with a few more
        /// clues than requested. That is the correct failure direction: slightly easier than
        /// asked for, never ambiguous. (17 is the proven minimum for any unique Sudoku, so
        /// Expert deliberately sits well clear of it.)
        /// </summary>
        public static int TargetGivens(Level level) => level switch
        {
            Level.Easy => 45,
            Level.Medium => 36,
            Level.Hard => 30,
            Level.Expert => 26,
            _ => 36,
        };

        public sealed record Puzzle(int[] Givens, int[] Solution);

        public static Puzzle Generate(ref DeterministicRng rng, Level level)
        {
            var solution = new int[Cells];
            FillFrom(solution, 0, ref rng);

            var givens = (int[])solution.Clone();

            var order = new List<int>(Cells);
            for (var i = 0; i < Cells; i++) order.Add(i);
            rng.Shuffle(order);

            var remaining = Cells;
            var target = TargetGivens(level);

            foreach (var cell in order)
            {
                if (remaining <= target) break;

                var saved = givens[cell];
                givens[cell] = 0;

                // Counting stops at two: whether a bad puzzle has two completions or two hundred
                // makes no difference to the decision, and bailing early is most of the speed.
                if (CountSolutions((int[])givens.Clone(), 2) == 1)
                {
                    remaining--;
                    continue;
                }

                givens[cell] = saved;
            }

            return new Puzzle(givens, solution);
        }

        /// <summary>
        /// Fills the grid cell by cell, trying digits in a shuffled order.
        ///
        /// The shuffle is the whole generator. Backtracking with digits tried 1..9 in order is
        /// deterministic and produces the same grid every time; shuffling the candidates at each
        /// cell is what turns one solved grid into 6.6×10²¹ of them.
        /// </summary>
        private static bool FillFrom(int[] grid, int index, ref DeterministicRng rng)
        {
            if (index == Cells) return true;

            var digits = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            rng.Shuffle(digits);

            foreach (var digit in digits)
            {
                if (!IsLegal(grid, index, digit)) continue;

                grid[index] = digit;
                if (FillFrom(grid, index + 1, ref rng)) return true;
                grid[index] = 0;
            }

            return false;
        }

        /// <summary>
        /// Counts completions, stopping once <paramref name="limit"/> is reached.
        ///
        /// Cells are chosen most-constrained-first rather than left-to-right. On a sparse grid
        /// that is the difference between a search that returns immediately and one that explores
        /// an enormous number of dead branches before noticing — and this runs about eighty times
        /// per generated puzzle, so it is worth the dozen extra lines.
        /// </summary>
        private static int CountSolutions(int[] grid, int limit)
        {
            var target = -1;
            var fewest = int.MaxValue;

            for (var i = 0; i < Cells; i++)
            {
                if (grid[i] != 0) continue;

                var options = 0;
                for (var digit = 1; digit <= Size; digit++)
                {
                    if (IsLegal(grid, i, digit)) options++;
                }

                // No legal digit anywhere means this branch is already contradictory.
                if (options == 0) return 0;

                if (options >= fewest) continue;

                fewest = options;
                target = i;

                // Forced cells cannot be beaten, so stop looking.
                if (options == 1) break;
            }

            if (target < 0) return 1;

            var found = 0;

            for (var digit = 1; digit <= Size; digit++)
            {
                if (!IsLegal(grid, target, digit)) continue;

                grid[target] = digit;
                found += CountSolutions(grid, limit - found);
                grid[target] = 0;

                if (found >= limit) return found;
            }

            return found;
        }

        private static bool IsLegal(int[] grid, int index, int digit)
        {
            var row = index / Size;
            var column = index % Size;

            for (var i = 0; i < Size; i++)
            {
                if (grid[row * Size + i] == digit) return false;
                if (grid[i * Size + column] == digit) return false;
            }

            var boxRow = row / 3 * 3;
            var boxColumn = column / 3 * 3;

            for (var r = boxRow; r < boxRow + 3; r++)
            {
                for (var c = boxColumn; c < boxColumn + 3; c++)
                {
                    if (grid[r * Size + c] == digit) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when the submission is the puzzle's solution.
        ///
        /// Compared against the stored solution rather than re-checked for row/column/box
        /// legality. Those are not the same test: a uniquely-solvable puzzle has exactly one
        /// legal completion, so any legal grid *is* the solution — but only if the clues were
        /// left alone. Comparing directly also catches a submission that overwrote a given,
        /// which a legality check would happily accept.
        /// </summary>
        public static bool IsSolved(IReadOnlyList<int> submitted, IReadOnlyList<int> solution)
        {
            if (submitted.Count != Cells || solution.Count != Cells) return false;

            for (var i = 0; i < Cells; i++)
            {
                if (submitted[i] != solution[i]) return false;
            }

            return true;
        }
    }
}
