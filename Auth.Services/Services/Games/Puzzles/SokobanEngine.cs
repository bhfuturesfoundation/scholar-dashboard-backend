using System.Text;

namespace Auth.Services.Services.Games.Puzzles
{
    /// <summary>
    /// Sokoban: push every box onto a goal, and do not paint yourself into a corner.
    ///
    /// ── Why the levels are original ──────────────────────────────────────────
    ///
    /// The classic sets — Thinking Rabbit's original 50, and most of the famous collections that
    /// followed — are copyrighted. Level design is authorship, not data, and "it is only a grid of
    /// characters" is not a licence. So these are written for this project.
    ///
    /// Every one is proven solvable by a breadth-first search in the test suite rather than by
    /// eye. That matters more here than in most puzzle games: an unsolvable Sokoban is not
    /// obviously broken, it just looks hard, and a player can lose an hour to a level that never
    /// had an answer.
    ///
    /// ── Why the score is moves, not time ─────────────────────────────────────
    ///
    /// Sokoban is a thinking game. Timing it would reward frantic pushing and punish the staring
    /// that the game is actually made of. Move count is the metric the genre has always used, and
    /// it rewards the thing worth rewarding: finding a shorter solution.
    /// </summary>
    public static class SokobanEngine
    {
        /// <summary>
        /// A level, in the notation every Sokoban editor has used for thirty years.
        ///
        /// <c>#</c> wall, <c>@</c> player, <c>$</c> box, <c>.</c> goal, <c>*</c> box already on a
        /// goal, <c>+</c> player standing on a goal, space floor.
        /// </summary>
        public sealed record Level(string Name, string[] Rows, int ParMoves);

        /// <summary>
        /// Ordered easiest to hardest, and deliberately gentle at the start: the first few teach
        /// one idea each — push, corner, order — before anything asks for a plan.
        /// </summary>
        public static readonly Level[] Levels =
        {
            new("First Push", new[]
            {
                "#######",
                "#     #",
                "# @$. #",
                "#     #",
                "#######",
            }, 2),

            new("Two Boxes", new[]
            {
                "########",
                "#      #",
                "# @$ . #",
                "#  $ . #",
                "#      #",
                "########",
            }, 12),

            new("Around The Corner", new[]
            {
                "########",
                "#  #   #",
                "# .$ @ #",
                "#  #   #",
                "#  #   #",
                "########",
            }, 6),

            new("Mind The Wall", new[]
            {
                "#########",
                "#       #",
                "# $###  #",
                "# @  .  #",
                "#       #",
                "#########",
            }, 10),

            new("Three In A Row", new[]
            {
                "##########",
                "#        #",
                "# $$$    #",
                "# @      #",
                "#   ...  #",
                "#        #",
                "##########",
            }, 30),

            // Replaced after the solvability test proved the original had no answer: its goals sat
            // where a box could only arrive by being pushed upward, from a square that was solid
            // wall. It looked like a hard level and was an impossible one, which is exactly the
            // failure the test exists to catch.
            new("Down The Hall", new[]
            {
                "#########",
                "#       #",
                "#  $ $  #",
                "#       #",
                "#   @   #",
                "#       #",
                "#  . .  #",
                "#       #",
                "#########",
            }, 20),

            new("Cross Purposes", new[]
            {
                "#########",
                "#   .   #",
                "#   $   #",
                "# .$@$. #",
                "#   $   #",
                "#   .   #",
                "#########",
            }, 20),

            new("Storeroom", new[]
            {
                "##########",
                "#  ....  #",
                "#  $$$$  #",
                "#        #",
                "#   @    #",
                "#        #",
                "##########",
            }, 24),
        };

        /// <summary>Up, Right, Down, Left — the order the client sends and the replay reads.</summary>
        private static readonly (int X, int Y)[] Directions = { (0, -1), (1, 0), (0, 1), (-1, 0) };

        public sealed class State
        {
            public required int Width { get; init; }
            public required int Height { get; init; }

            /// <summary>Static geometry: walls and goals. Never changes once parsed.</summary>
            public required bool[] Walls { get; init; }
            public required bool[] Goals { get; init; }

            /// <summary>What moves.</summary>
            public required bool[] Boxes { get; set; }
            public required int PlayerX { get; set; }
            public required int PlayerY { get; set; }

            public int Index(int x, int y) => y * Width + x;
        }

        public static State Parse(Level level)
        {
            var height = level.Rows.Length;
            var width = 0;
            foreach (var row in level.Rows) width = Math.Max(width, row.Length);

            var walls = new bool[width * height];
            var goals = new bool[width * height];
            var boxes = new bool[width * height];
            var playerX = 0;
            var playerY = 0;

            for (var y = 0; y < height; y++)
            {
                var row = level.Rows[y];

                for (var x = 0; x < width; x++)
                {
                    // Rows may be ragged in hand-written levels; anything past the end is floor
                    // enclosed by the wall on the row above and below.
                    var cell = x < row.Length ? row[x] : ' ';
                    var i = y * width + x;

                    switch (cell)
                    {
                        case '#': walls[i] = true; break;
                        case '.': goals[i] = true; break;
                        case '$': boxes[i] = true; break;
                        case '*': boxes[i] = true; goals[i] = true; break;
                        case '@': playerX = x; playerY = y; break;
                        case '+': playerX = x; playerY = y; goals[i] = true; break;
                    }
                }
            }

            return new State
            {
                Width = width,
                Height = height,
                Walls = walls,
                Goals = goals,
                Boxes = boxes,
                PlayerX = playerX,
                PlayerY = playerY,
            };
        }

        /// <summary>
        /// Applies one step. Returns false when nothing moved, which the replay treats as a
        /// rejection — an honest client never sends a move it did not make.
        /// </summary>
        public static bool TryMove(State state, int direction)
        {
            if (direction is < 0 or > 3) return false;

            var (dx, dy) = Directions[direction];

            var nextX = state.PlayerX + dx;
            var nextY = state.PlayerY + dy;
            if (!InBounds(state, nextX, nextY)) return false;

            var next = state.Index(nextX, nextY);
            if (state.Walls[next]) return false;

            if (state.Boxes[next])
            {
                // Pushing: the cell beyond the box has to be free. Two boxes in a line cannot be
                // pushed together — that rule is most of what makes the game hard.
                var beyondX = nextX + dx;
                var beyondY = nextY + dy;
                if (!InBounds(state, beyondX, beyondY)) return false;

                var beyond = state.Index(beyondX, beyondY);
                if (state.Walls[beyond] || state.Boxes[beyond]) return false;

                state.Boxes[next] = false;
                state.Boxes[beyond] = true;
            }

            state.PlayerX = nextX;
            state.PlayerY = nextY;
            return true;
        }

        private static bool InBounds(State state, int x, int y) =>
            x >= 0 && x < state.Width && y >= 0 && y < state.Height;

        public static bool IsSolved(State state)
        {
            for (var i = 0; i < state.Boxes.Length; i++)
            {
                if (state.Boxes[i] && !state.Goals[i]) return false;
            }

            return true;
        }

        /// <summary>A compact key for visited-set membership in a search.</summary>
        public static string Signature(State state)
        {
            var builder = new StringBuilder();
            builder.Append(state.PlayerX).Append(',').Append(state.PlayerY).Append('|');

            for (var i = 0; i < state.Boxes.Length; i++)
            {
                if (state.Boxes[i]) builder.Append(i).Append(',');
            }

            return builder.ToString();
        }

        public static State Clone(State state) => new()
        {
            Width = state.Width,
            Height = state.Height,
            Walls = state.Walls,
            Goals = state.Goals,
            Boxes = (bool[])state.Boxes.Clone(),
            PlayerX = state.PlayerX,
            PlayerY = state.PlayerY,
        };

        /// <summary>A generous ceiling. The longest par here is under forty.</summary>
        public const int MaxMoves = 5_000;

        public sealed record ReplayResult(bool Valid, bool Solved, int Moves, string? Rejection);

        public static ReplayResult Replay(int levelIndex, IReadOnlyList<int> moves)
        {
            if (levelIndex < 0 || levelIndex >= Levels.Length)
                return new ReplayResult(false, false, 0, "There is no such level.");

            if (moves.Count > MaxMoves)
                return new ReplayResult(false, false, 0, "Move log is too long.");

            var state = Parse(Levels[levelIndex]);

            for (var i = 0; i < moves.Count; i++)
            {
                if (!TryMove(state, moves[i]))
                    return new ReplayResult(false, false, i, $"Move {i} could not be made.");
            }

            return new ReplayResult(true, IsSolved(state), moves.Count, null);
        }
    }
}
