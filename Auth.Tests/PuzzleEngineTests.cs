using Auth.Services.Services.Games.Puzzles;

namespace Auth.Tests;

/// <summary>
/// Tests for the puzzle engines.
///
/// Same reasoning as the arena tests: these engines *are* the leaderboard. A submitted game is
/// not checked against a client's claim, it is re-run here and whatever this code computes is
/// what stands. So the properties worth pinning are the ones that would let a wrong score
/// through quietly — replay determinism above all, since every guarantee in this design rests
/// on the same seed and the same moves producing the same board on every machine, forever.
/// </summary>
public class PuzzleEngineTests
{
    // ── 2048 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Collapse_MergesEqualNeighbours()
    {
        var board = new int[16];
        board[0] = 2;
        board[1] = 2;

        var outcome = Game2048Engine.ApplyMove(board, Game2048Engine.Direction.Left);

        Assert.True(outcome.Changed);
        Assert.Equal(4, outcome.ScoreGained);
        Assert.Equal(4, board[0]);
        Assert.Equal(0, board[1]);
    }

    /// <summary>
    /// The rule that keeps the board filling up. A row of 2 2 4 must become 4 4, never 8 — if a
    /// tile can merge twice in one move, chaining becomes free and the game stops being hard.
    /// </summary>
    [Fact]
    public void Collapse_DoesNotMergeTheSameTileTwice()
    {
        var board = new int[16];
        board[0] = 2;
        board[1] = 2;
        board[2] = 4;

        var outcome = Game2048Engine.ApplyMove(board, Game2048Engine.Direction.Left);

        Assert.Equal(4, board[0]);
        Assert.Equal(4, board[1]);
        Assert.Equal(0, board[2]);
        Assert.Equal(4, outcome.ScoreGained);
    }

    [Fact]
    public void Collapse_SlidesWithoutMergingUnequalTiles()
    {
        var board = new int[16];
        board[2] = 2;
        board[3] = 4;

        var outcome = Game2048Engine.ApplyMove(board, Game2048Engine.Direction.Left);

        Assert.True(outcome.Changed);
        Assert.Equal(0, outcome.ScoreGained);
        Assert.Equal(2, board[0]);
        Assert.Equal(4, board[1]);
    }

    /// <summary>
    /// A move against a wall that changes nothing must report it, because the caller uses that
    /// flag to decide whether to spawn. Spawning anyway is the classic 2048 bug that makes the
    /// game unwinnable by pressing a direction repeatedly.
    /// </summary>
    [Fact]
    public void ApplyMove_ReportsNoChangeWhenAlreadyFlush()
    {
        var board = new int[16];
        board[0] = 2;
        board[4] = 4;

        var outcome = Game2048Engine.ApplyMove(board, Game2048Engine.Direction.Left);

        Assert.False(outcome.Changed);
        Assert.Equal(0, outcome.ScoreGained);
    }

    /// <summary>
    /// Two tiles side by side in the middle of the board. Every direction must move them, but
    /// only the two along their shared row may merge them — a vertical move slides two tiles that
    /// are in different columns and must leave both at 2.
    /// </summary>
    [Fact]
    public void ApplyMove_WorksInEveryDirection()
    {
        foreach (var direction in Enum.GetValues<Game2048Engine.Direction>())
        {
            var board = new int[16];
            board[5] = 2;
            board[6] = 2;

            var outcome = Game2048Engine.ApplyMove(board, direction);
            var merges = direction is Game2048Engine.Direction.Left or Game2048Engine.Direction.Right;

            Assert.True(outcome.Changed, $"{direction} moved nothing.");
            Assert.Equal(merges ? 4 : 2, Game2048Engine.HighestTile(board));
            Assert.Equal(merges ? 4 : 0, outcome.ScoreGained);
            Assert.Equal(merges ? 1 : 2, board.Count(v => v != 0));
        }
    }

    [Fact]
    public void Deal_PlacesExactlyTwoTiles()
    {
        var rng = new DeterministicRng(12345);
        var board = Game2048Engine.Deal(ref rng);

        Assert.Equal(2, board.Count(v => v != 0));
        Assert.All(board.Where(v => v != 0), v => Assert.True(v is 2 or 4));
    }

    [Fact]
    public void IsGameOver_FalseWhileAMergeRemains()
    {
        // A full board with two equal neighbours is still playable.
        var mergeable = Enumerable.Range(0, 16).Select(i => i + 1).ToArray();
        mergeable[15] = mergeable[14];
        Assert.False(Game2048Engine.IsGameOver(mergeable));

        // An empty cell is playable regardless of what surrounds it.
        var spacious = Enumerable.Range(0, 16).Select(i => i + 1).ToArray();
        spacious[7] = 0;
        Assert.False(Game2048Engine.IsGameOver(spacious));

        // A checkerboard of alternating values has no empty cell and no equal neighbours.
        var stuck = new int[16];
        for (var i = 0; i < 16; i++)
        {
            var row = i / 4;
            var column = i % 4;
            stuck[i] = (row + column) % 2 == 0 ? 2 : 4;
        }

        Assert.True(Game2048Engine.IsGameOver(stuck));
    }

    /// <summary>
    /// The property the whole verification model rests on. Two replays of the same seed and the
    /// same moves must agree exactly — if this ever fails, every recorded score becomes
    /// unprovable and the failure looks like cheating rather than like a bug.
    /// </summary>
    [Fact]
    public void Replay_IsDeterministic()
    {
        var moves = PlayableMoves(seed: 777, count: 60);

        var first = Game2048Engine.Replay(777, moves, 30_000);
        var second = Game2048Engine.Replay(777, moves, 30_000);

        Assert.True(first.Valid);
        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.HighestTile, second.HighestTile);
    }

    [Fact]
    public void Replay_RejectsAMoveThatChangesNothing()
    {
        // Left twice from the deal: the second cannot move anything that the first did not.
        var rng = new DeterministicRng(4242);
        var board = Game2048Engine.Deal(ref rng);
        Game2048Engine.ApplyMove(board, Game2048Engine.Direction.Left);
        Game2048Engine.SpawnTile(board, ref rng);

        var padded = new List<int> { (int)Game2048Engine.Direction.Left };
        for (var i = 0; i < 40; i++) padded.Add((int)Game2048Engine.Direction.Left);

        var result = Game2048Engine.Replay(4242, padded, 30_000);

        Assert.False(result.Valid);
        Assert.NotNull(result.Rejection);
    }

    [Fact]
    public void Replay_RejectsAnUnknownDirection()
    {
        var result = Game2048Engine.Replay(1, new[] { 9 }, 30_000);

        Assert.False(result.Valid);
        Assert.Equal(0, result.Score);
    }

    [Fact]
    public void Replay_RejectsALogLongerThanTheCap()
    {
        var result = Game2048Engine.Replay(1, new int[50], maxMoves: 10);

        Assert.False(result.Valid);
    }

    [Fact]
    public void Replay_ScoresTheSumOfMerges()
    {
        // An empty log is a valid game that simply scored nothing.
        var result = Game2048Engine.Replay(99, Array.Empty<int>(), 30_000);

        Assert.True(result.Valid);
        Assert.Equal(0, result.Score);
        Assert.Equal(0, result.MovesPlayed);
    }

    /// <summary>Plays greedily to produce a log that a real client could have produced.</summary>
    private static List<int> PlayableMoves(uint seed, int count)
    {
        var rng = new DeterministicRng(seed);
        var board = Game2048Engine.Deal(ref rng);
        var moves = new List<int>();

        var cursor = new DeterministicRng(seed ^ 0x5bf03635);

        while (moves.Count < count && !Game2048Engine.IsGameOver(board))
        {
            var direction = cursor.Next(4);
            var probe = (int[])board.Clone();

            if (!Game2048Engine.ApplyMove(probe, (Game2048Engine.Direction)direction).Changed) continue;

            Game2048Engine.ApplyMove(board, (Game2048Engine.Direction)direction);
            Game2048Engine.SpawnTile(board, ref rng);
            moves.Add(direction);
        }

        return moves;
    }

    // ── Sudoku ────────────────────────────────────────────────────────────

    [Fact]
    public void Generate_ProducesALegalCompleteSolution()
    {
        var rng = new DeterministicRng(2024);
        var puzzle = SudokuEngine.Generate(ref rng, SudokuEngine.Level.Medium);

        Assert.Equal(81, puzzle.Solution.Length);
        Assert.All(puzzle.Solution, digit => Assert.InRange(digit, 1, 9));

        for (var row = 0; row < 9; row++)
        {
            var rowDigits = new HashSet<int>();
            var columnDigits = new HashSet<int>();

            for (var i = 0; i < 9; i++)
            {
                Assert.True(rowDigits.Add(puzzle.Solution[row * 9 + i]), $"Row {row} repeats a digit.");
                Assert.True(columnDigits.Add(puzzle.Solution[i * 9 + row]), $"Column {row} repeats a digit.");
            }
        }

        for (var box = 0; box < 9; box++)
        {
            var digits = new HashSet<int>();
            var baseRow = box / 3 * 3;
            var baseColumn = box % 3 * 3;

            for (var r = 0; r < 3; r++)
            {
                for (var c = 0; c < 3; c++)
                {
                    Assert.True(digits.Add(puzzle.Solution[(baseRow + r) * 9 + baseColumn + c]), $"Box {box} repeats.");
                }
            }
        }
    }

    [Fact]
    public void Generate_LeavesEveryClueAgreeingWithTheSolution()
    {
        var rng = new DeterministicRng(31337);
        var puzzle = SudokuEngine.Generate(ref rng, SudokuEngine.Level.Hard);

        for (var i = 0; i < 81; i++)
        {
            if (puzzle.Givens[i] == 0) continue;
            Assert.Equal(puzzle.Solution[i], puzzle.Givens[i]);
        }
    }

    /// <summary>
    /// The property that makes a grid a puzzle rather than a guess. Verified independently of the
    /// generator's own check by brute-force counting completions of the clues it handed out.
    /// </summary>
    [Theory]
    [InlineData(SudokuEngine.Level.Easy)]
    [InlineData(SudokuEngine.Level.Medium)]
    [InlineData(SudokuEngine.Level.Hard)]
    [InlineData(SudokuEngine.Level.Expert)]
    public void Generate_ProducesExactlyOneSolution(SudokuEngine.Level level)
    {
        var rng = new DeterministicRng(8191);
        var puzzle = SudokuEngine.Generate(ref rng, level);

        Assert.Equal(1, BruteForceCount((int[])puzzle.Givens.Clone(), 2));
    }

    [Fact]
    public void Generate_HitsRoughlyTheRequestedClueCount()
    {
        var rng = new DeterministicRng(5150);
        var puzzle = SudokuEngine.Generate(ref rng, SudokuEngine.Level.Easy);

        var clues = puzzle.Givens.Count(v => v != 0);

        // Digging stops early when nothing more can come out uniquely, so the target is a floor
        // to approach rather than a number to hit. An easy board should never be near-blank.
        Assert.InRange(clues, SudokuEngine.TargetGivens(SudokuEngine.Level.Easy), 81);
    }

    [Fact]
    public void Generate_IsReproducibleFromItsSeed()
    {
        var a = new DeterministicRng(60613);
        var b = new DeterministicRng(60613);

        var first = SudokuEngine.Generate(ref a, SudokuEngine.Level.Medium);
        var second = SudokuEngine.Generate(ref b, SudokuEngine.Level.Medium);

        Assert.Equal(first.Solution, second.Solution);
        Assert.Equal(first.Givens, second.Givens);
    }

    [Fact]
    public void IsSolved_RejectsAnythingButTheSolution()
    {
        var rng = new DeterministicRng(1024);
        var puzzle = SudokuEngine.Generate(ref rng, SudokuEngine.Level.Easy);

        Assert.True(SudokuEngine.IsSolved(puzzle.Solution, puzzle.Solution));

        var tampered = (int[])puzzle.Solution.Clone();
        tampered[40] = tampered[40] == 9 ? 1 : tampered[40] + 1;
        Assert.False(SudokuEngine.IsSolved(tampered, puzzle.Solution));

        Assert.False(SudokuEngine.IsSolved(new int[80], puzzle.Solution));
        Assert.False(SudokuEngine.IsSolved(puzzle.Givens, puzzle.Solution));
    }

    /// <summary>Independent of the engine's own solver, so the two cannot agree on a shared bug.</summary>
    private static int BruteForceCount(int[] grid, int limit)
    {
        var index = Array.IndexOf(grid, 0);
        if (index < 0) return 1;

        var found = 0;

        for (var digit = 1; digit <= 9; digit++)
        {
            if (!Legal(grid, index, digit)) continue;

            grid[index] = digit;
            found += BruteForceCount(grid, limit - found);
            grid[index] = 0;

            if (found >= limit) return found;
        }

        return found;

        static bool Legal(int[] g, int index, int digit)
        {
            var row = index / 9;
            var column = index % 9;

            for (var i = 0; i < 9; i++)
            {
                if (g[row * 9 + i] == digit || g[i * 9 + column] == digit) return false;
            }

            for (var r = row / 3 * 3; r < row / 3 * 3 + 3; r++)
            {
                for (var c = column / 3 * 3; c < column / 3 * 3 + 3; c++)
                {
                    if (g[r * 9 + c] == digit) return false;
                }
            }

            return true;
        }
    }

    // ── Minesweeper ───────────────────────────────────────────────────────

    /// <summary>
    /// The rule that turns the opening from a coin flip into a position: the first click and all
    /// eight of its neighbours must be clear, so it always cascades.
    /// </summary>
    [Theory]
    [InlineData(MinesweeperEngine.Level.Beginner)]
    [InlineData(MinesweeperEngine.Level.Intermediate)]
    [InlineData(MinesweeperEngine.Level.Expert)]
    public void Generate_KeepsTheFirstClickAndItsNeighboursClear(MinesweeperEngine.Level level)
    {
        var spec = MinesweeperEngine.SpecFor(level);
        var rng = new DeterministicRng(4711);

        // A corner is the tightest case: fewest neighbours to exclude, most mines per free cell.
        var board = MinesweeperEngine.Generate(spec, 0, ref rng);

        Assert.False(board.Mines[0]);
        foreach (var neighbour in MinesweeperEngine.Neighbours(spec, 0))
        {
            Assert.False(board.Mines[neighbour], $"Cell {neighbour} next to the first click is mined.");
        }

        Assert.Equal(spec.Mines, board.Mines.Count(m => m));
    }

    [Fact]
    public void Generate_CountsAdjacencyCorrectly()
    {
        var spec = MinesweeperEngine.SpecFor(MinesweeperEngine.Level.Intermediate);
        var rng = new DeterministicRng(9001);
        var board = MinesweeperEngine.Generate(spec, 40, ref rng);

        for (var i = 0; i < spec.Cells; i++)
        {
            if (board.Mines[i]) continue;

            var expected = MinesweeperEngine.Neighbours(spec, i).Count(n => board.Mines[n]);
            Assert.Equal(expected, board.Adjacent[i]);
        }
    }

    [Fact]
    public void Reveal_CascadesThroughEmptyRegionsAndStopsAtNumbers()
    {
        var spec = MinesweeperEngine.SpecFor(MinesweeperEngine.Level.Beginner);
        var rng = new DeterministicRng(1337);
        var board = MinesweeperEngine.Generate(spec, 0, ref rng);

        var revealed = new bool[spec.Cells];
        MinesweeperEngine.Reveal(board, revealed, 0);

        // The first click is guaranteed to sit in a zero-adjacency region, so it opens more
        // than itself — that is the entire point of the safe-opening rule.
        Assert.True(revealed.Count(r => r) > 1);

        // Nothing mined is ever opened by a cascade, and every opened number is a boundary.
        for (var i = 0; i < spec.Cells; i++)
        {
            if (!revealed[i]) continue;

            Assert.False(board.Mines[i]);

            if (board.Adjacent[i] != 0) continue;

            foreach (var neighbour in MinesweeperEngine.Neighbours(spec, i))
            {
                Assert.True(revealed[neighbour], "A zero cell left a neighbour closed.");
            }
        }
    }

    [Fact]
    public void Replay_AcceptsAClearedBoardAndIsDeterministic()
    {
        var spec = MinesweeperEngine.SpecFor(MinesweeperEngine.Level.Beginner);
        var reveals = SolveHonestly(spec, seed: 24680, MinesweeperEngine.Level.Beginner);

        var first = MinesweeperEngine.Replay(24680, MinesweeperEngine.Level.Beginner, reveals);
        var second = MinesweeperEngine.Replay(24680, MinesweeperEngine.Level.Beginner, reveals);

        Assert.True(first.Valid);
        Assert.True(first.Cleared);
        Assert.Equal(spec.Cells - spec.Mines, first.Revealed);
        Assert.Equal(first.Revealed, second.Revealed);
    }

    [Fact]
    public void Replay_RejectsALogThatOpensAMine()
    {
        var spec = MinesweeperEngine.SpecFor(MinesweeperEngine.Level.Beginner);
        var rng = new DeterministicRng(555);
        var board = MinesweeperEngine.Generate(spec, 0, ref rng);

        var mine = Array.IndexOf(board.Mines, true);
        var result = MinesweeperEngine.Replay(555, MinesweeperEngine.Level.Beginner, new[] { 0, mine });

        Assert.False(result.Valid);
        Assert.Contains("mine", result.Rejection!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Replay_RejectsAnEmptyOrOutOfBoundsLog()
    {
        Assert.False(MinesweeperEngine.Replay(1, MinesweeperEngine.Level.Beginner, Array.Empty<int>()).Valid);
        Assert.False(MinesweeperEngine.Replay(1, MinesweeperEngine.Level.Beginner, new[] { -1 }).Valid);
        Assert.False(MinesweeperEngine.Replay(1, MinesweeperEngine.Level.Beginner, new[] { 9999 }).Valid);
    }

    /// <summary>
    /// Duplicates are tolerated deliberately — a double tap on a phone must not throw away a won
    /// board — so this pins that leniency rather than leaving it to chance.
    /// </summary>
    [Fact]
    public void Replay_IgnoresRepeatedReveals()
    {
        var spec = MinesweeperEngine.SpecFor(MinesweeperEngine.Level.Beginner);
        var reveals = SolveHonestly(spec, 13579, MinesweeperEngine.Level.Beginner);

        var doubled = reveals.SelectMany(cell => new[] { cell, cell }).ToArray();
        var result = MinesweeperEngine.Replay(13579, MinesweeperEngine.Level.Beginner, doubled);

        Assert.True(result.Valid);
        Assert.True(result.Cleared);
    }

    [Fact]
    public void Replay_RejectsAnIncompleteBoard()
    {
        var result = MinesweeperEngine.Replay(2468, MinesweeperEngine.Level.Beginner, new[] { 0 });

        Assert.True(result.Valid);
        Assert.False(result.Cleared);
    }

    /// <summary>Opens every safe cell, in the order an omniscient but honest client would.</summary>
    private static int[] SolveHonestly(MinesweeperEngine.BoardSpec spec, uint seed, MinesweeperEngine.Level level)
    {
        var rng = new DeterministicRng(seed);
        var board = MinesweeperEngine.Generate(spec, 0, ref rng);

        var reveals = new List<int> { 0 };
        for (var i = 0; i < spec.Cells; i++)
        {
            if (!board.Mines[i]) reveals.Add(i);
        }

        return reveals.ToArray();
    }

    // ── Tickets ───────────────────────────────────────────────────────────

    private static PuzzleTicket SampleTicket(string userId = "user-1") => new()
    {
        UserId = userId,
        GameId = "sudoku",
        Seed = 123456,
        Difficulty = 1,
        DealtAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Nonce = "ABCDEF",
    };

    [Fact]
    public void Ticket_RoundTripsWhatItSigned()
    {
        var signer = new PuzzleTicketSigner("a-secret-long-enough-to-be-real");
        var ticket = SampleTicket();

        var verified = signer.Verify(signer.Sign(ticket), "user-1");

        Assert.NotNull(verified);
        Assert.Equal(ticket.Seed, verified!.Seed);
        Assert.Equal(ticket.Difficulty, verified.Difficulty);
        Assert.Equal(ticket.DealtAtUnixMs, verified.DealtAtUnixMs);
    }

    /// <summary>
    /// The attack the signature exists to stop: swapping in the seed of a board you already
    /// solved offline. Any edit to the payload must invalidate the token.
    /// </summary>
    [Fact]
    public void Ticket_RejectsATamperedPayload()
    {
        var signer = new PuzzleTicketSigner("a-secret-long-enough-to-be-real");
        var token = signer.Sign(SampleTicket());

        var parts = token.Split('.');
        var tamperedPayload = 'A' + parts[0][1..];

        Assert.Null(signer.Verify($"{tamperedPayload}.{parts[1]}", "user-1"));
        Assert.Null(signer.Verify($"{parts[0]}.{parts[1][..^2]}AA", "user-1"));
        Assert.Null(signer.Verify(parts[0], "user-1"));
        Assert.Null(signer.Verify("", "user-1"));
        Assert.Null(signer.Verify(null, "user-1"));
    }

    /// <summary>A valid signature proves the server issued it, not that it issued it to you.</summary>
    [Fact]
    public void Ticket_RejectsSomebodyElsesTicket()
    {
        var signer = new PuzzleTicketSigner("a-secret-long-enough-to-be-real");
        var token = signer.Sign(SampleTicket("user-1"));

        Assert.Null(signer.Verify(token, "user-2"));
    }

    [Fact]
    public void Ticket_RejectsOneSignedWithADifferentSecret()
    {
        var mine = new PuzzleTicketSigner("a-secret-long-enough-to-be-real");
        var theirs = new PuzzleTicketSigner("a-different-secret-entirely");

        Assert.Null(mine.Verify(theirs.Sign(SampleTicket()), "user-1"));
    }

    /// <summary>
    /// A signed ticket must not be a permanent licence to submit a score for that board — solve
    /// it offline over a week and submit whenever the leaderboard is most convenient.
    /// </summary>
    [Fact]
    public void Ticket_RejectsOneOlderThanTheWindow()
    {
        var signer = new PuzzleTicketSigner("a-secret-long-enough-to-be-real");

        var stale = SampleTicket() with
        {
            DealtAtUnixMs = DateTimeOffset.UtcNow
                .Subtract(PuzzleTicketSigner.MaxAge + TimeSpan.FromMinutes(1))
                .ToUnixTimeMilliseconds(),
        };

        Assert.Null(signer.Verify(signer.Sign(stale), "user-1"));
    }

    /// <summary>Backdating is the other half of faking a fast finish.</summary>
    [Fact]
    public void Ticket_RejectsOneDatedInTheFuture()
    {
        var signer = new PuzzleTicketSigner("a-secret-long-enough-to-be-real");

        var ahead = SampleTicket() with
        {
            DealtAtUnixMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(),
        };

        Assert.Null(signer.Verify(signer.Sign(ahead), "user-1"));
    }

    [Fact]
    public void Ticket_RequiresASecret()
    {
        Assert.Throws<ArgumentException>(() => new PuzzleTicketSigner(""));
    }

    // ── Scoring ───────────────────────────────────────────────────────────

    [Fact]
    public void FromTime_PaysBasePointsAtPar()
    {
        var curve = PuzzleScoring.ForSudoku(SudokuEngine.Level.Medium);

        Assert.Equal(curve.BasePoints, PuzzleScoring.FromTime(curve, curve.ParSeconds));
    }

    /// <summary>
    /// Faster is worth more but is bounded, so an implausibly instant finish is worth no more
    /// than a very good one — which removes most of the incentive to script it.
    /// </summary>
    [Fact]
    public void FromTime_RewardsSpeedButCapsAtDoubleBase()
    {
        var curve = PuzzleScoring.ForMinesweeper(MinesweeperEngine.Level.Expert);

        var quick = PuzzleScoring.FromTime(curve, curve.ParSeconds / 2);
        var atPar = PuzzleScoring.FromTime(curve, curve.ParSeconds);

        Assert.True(quick > atPar);
        Assert.True(PuzzleScoring.FromTime(curve, 0) <= curve.BasePoints * 2);
    }

    /// <summary>Never negative: finishing slowly is still finishing.</summary>
    [Fact]
    public void FromTime_DecaysTowardZeroWithoutGoingNegative()
    {
        var curve = PuzzleScoring.ForSudoku(SudokuEngine.Level.Easy);

        var slow = PuzzleScoring.FromTime(curve, curve.ParSeconds * 20);

        Assert.True(slow > 0);
        Assert.True(slow < curve.BasePoints / 4);
    }

    [Fact]
    public void ApplyHintPenalty_CostsPerHintDownToAFloor()
    {
        Assert.Equal(1000, PuzzleScoring.ApplyHintPenalty(1000, 0));
        Assert.True(PuzzleScoring.ApplyHintPenalty(1000, 1) < 1000);
        Assert.True(PuzzleScoring.ApplyHintPenalty(1000, 3) < PuzzleScoring.ApplyHintPenalty(1000, 1));
        Assert.Equal(250, PuzzleScoring.ApplyHintPenalty(1000, 40));
    }

    // ── The generator itself ──────────────────────────────────────────────

    /// <summary>
    /// Pinned literally. Every score ever recorded assumes this sequence, so a change here — a
    /// "tidy-up" of the shift constants, say — must fail loudly rather than silently invalidate
    /// the leaderboard.
    /// </summary>
    [Fact]
    public void Rng_ProducesAStableSequence()
    {
        var rng = new DeterministicRng(1);
        var values = new[] { rng.NextUInt(), rng.NextUInt(), rng.NextUInt() };

        Assert.Equal(new uint[] { 270369, 67634689, 2647435461 }, values);
    }

    [Fact]
    public void Rng_NeverGetsStuckOnZero()
    {
        var rng = new DeterministicRng(0);

        Assert.NotEqual(0u, rng.NextUInt());
        Assert.NotEqual(0u, rng.NextUInt());
    }

    [Fact]
    public void Rng_StaysInRange()
    {
        var rng = new DeterministicRng(88);

        for (var i = 0; i < 500; i++)
        {
            Assert.InRange(rng.Next(7), 0, 6);
            Assert.InRange(rng.NextDouble(), 0.0, 1.0);
        }

        Assert.Equal(0, rng.Next(1));
        Assert.Equal(0, rng.Next(0));
    }

    [Fact]
    public void Shuffle_KeepsEveryElement()
    {
        var rng = new DeterministicRng(4096);
        var items = Enumerable.Range(0, 50).ToList();

        rng.Shuffle(items);

        Assert.Equal(50, items.Count);
        Assert.Equal(Enumerable.Range(0, 50), items.OrderBy(v => v));
    }

    // ── Tetris ────────────────────────────────────────────────────────────

    /// <summary>
    /// Every piece must occupy exactly four distinct cells in all four rotations. A typo in the
    /// shape table is otherwise invisible until someone reports that a piece "looks wrong", and
    /// by then it has been in scored games for weeks.
    /// </summary>
    [Fact]
    public void Shapes_AreFourCellsInEveryRotation()
    {
        foreach (var piece in Enum.GetValues<TetrisEngine.Piece>())
        {
            for (var rotation = 0; rotation < 4; rotation++)
            {
                var cells = TetrisEngine.CellsOf(piece, rotation, 0, 0);

                Assert.Equal(4, cells.Length);
                Assert.Equal(4, cells.Distinct().Count());
                Assert.All(cells, c => Assert.InRange(c.X, 0, 3));
                Assert.All(cells, c => Assert.InRange(c.Y, 0, 3));
            }
        }
    }

    /// <summary>Rotating O must be a no-op — it is the one piece SRS leaves alone.</summary>
    [Fact]
    public void Shapes_LeaveTheOPieceUnrotated()
    {
        var spawn = TetrisEngine.CellsOf(TetrisEngine.Piece.O, 0, 0, 0);

        for (var rotation = 1; rotation < 4; rotation++)
        {
            Assert.Equal(spawn, TetrisEngine.CellsOf(TetrisEngine.Piece.O, rotation, 0, 0));
        }
    }

    /// <summary>Four cells that do not touch are not a tetromino.</summary>
    [Fact]
    public void Shapes_AreConnected()
    {
        foreach (var piece in Enum.GetValues<TetrisEngine.Piece>())
        {
            for (var rotation = 0; rotation < 4; rotation++)
            {
                var cells = TetrisEngine.CellsOf(piece, rotation, 0, 0).ToHashSet();

                var reached = new HashSet<(int X, int Y)>();
                var stack = new Stack<(int X, int Y)>();
                stack.Push(cells.First());

                while (stack.Count > 0)
                {
                    var cell = stack.Pop();
                    if (!reached.Add(cell)) continue;

                    var neighbours = new[]
                    {
                        (cell.X + 1, cell.Y), (cell.X - 1, cell.Y),
                        (cell.X, cell.Y + 1), (cell.X, cell.Y - 1),
                    };

                    foreach (var next in neighbours)
                    {
                        if (cells.Contains(next)) stack.Push(next);
                    }
                }

                Assert.Equal(4, reached.Count);
            }
        }
    }

    /// <summary>
    /// The bag is why the game rewards planning rather than luck: seven pieces, each exactly
    /// once, so an I-piece can never be withheld for thirty placements.
    /// </summary>
    [Fact]
    public void Bag_ContainsEveryPieceExactlyOnce()
    {
        var rng = new DeterministicRng(90210);

        for (var round = 0; round < 40; round++)
        {
            var bag = TetrisEngine.Bag(ref rng);

            Assert.Equal(7, bag.Length);
            Assert.Equal(7, bag.Distinct().Count());
        }
    }

    [Fact]
    public void Bag_IsReproducibleFromItsSeed()
    {
        var a = new DeterministicRng(5150);
        var b = new DeterministicRng(5150);

        Assert.Equal(TetrisEngine.Bag(ref a), TetrisEngine.Bag(ref b));
    }

    [Fact]
    public void ClearLines_RemovesFullRowsAndDropsWhatWasAbove()
    {
        var board = new bool[TetrisEngine.Cells];

        for (var x = 0; x < TetrisEngine.Width; x++) board[19 * TetrisEngine.Width + x] = true;
        board[18 * TetrisEngine.Width + 3] = true;

        var cleared = TetrisEngine.ClearLines(board);

        Assert.Equal(1, cleared);

        // The stray cell fell one row, into what is now the bottom.
        Assert.True(board[19 * TetrisEngine.Width + 3]);
        Assert.Equal(1, board.Count(c => c));
    }

    /// <summary>Four at once, which is the clear the whole scoring table is built around.</summary>
    [Fact]
    public void ClearLines_HandlesFourRowsInOnePass()
    {
        var board = new bool[TetrisEngine.Cells];

        for (var y = 16; y <= 19; y++)
        {
            for (var x = 0; x < TetrisEngine.Width; x++) board[y * TetrisEngine.Width + x] = true;
        }

        board[15 * TetrisEngine.Width] = true;

        Assert.Equal(4, TetrisEngine.ClearLines(board));
        Assert.True(board[19 * TetrisEngine.Width]);
        Assert.Equal(1, board.Count(c => c));
    }

    [Fact]
    public void ClearLines_LeavesAnIncompleteRowAlone()
    {
        var board = new bool[TetrisEngine.Cells];
        for (var x = 0; x < TetrisEngine.Width - 1; x++) board[19 * TetrisEngine.Width + x] = true;

        Assert.Equal(0, TetrisEngine.ClearLines(board));
        Assert.Equal(TetrisEngine.Width - 1, board.Count(c => c));
    }

    /// <summary>Flat on the floor is legal; the same placement one row higher is not.</summary>
    [Fact]
    public void TetrisReplay_RequiresEachPlacementToRestOnSomething()
    {
        var rng = new DeterministicRng(2024);
        var first = TetrisEngine.Bag(ref rng)[0];
        var floorY = 19 - TetrisEngine.CellsOf(first, 0, 0, 0).Max(c => c.Y);

        var onFloor = TetrisEngine.Replay(2024, new[] { new TetrisEngine.Placement(0, 0, floorY, false) });
        Assert.True(onFloor.Valid);

        var floating = TetrisEngine.Replay(2024, new[] { new TetrisEngine.Placement(0, 0, floorY - 1, false) });
        Assert.False(floating.Valid);
        Assert.Contains("rest", floating.Rejection!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TetrisReplay_RejectsPlacementsOffTheBoardOrOverlapping()
    {
        var rng = new DeterministicRng(31337);
        var first = TetrisEngine.Bag(ref rng)[0];
        var floorY = 19 - TetrisEngine.CellsOf(first, 0, 0, 0).Max(c => c.Y);

        Assert.False(TetrisEngine.Replay(31337, new[] { new TetrisEngine.Placement(0, -5, floorY, false) }).Valid);
        Assert.False(TetrisEngine.Replay(31337, new[] { new TetrisEngine.Placement(0, 50, floorY, false) }).Valid);

        // Two identical placements: the second lands inside the first.
        var twice = new[]
        {
            new TetrisEngine.Placement(0, 0, floorY, false),
            new TetrisEngine.Placement(0, 0, floorY, false),
        };
        Assert.False(TetrisEngine.Replay(31337, twice).Valid);
    }

    [Fact]
    public void TetrisReplay_ScoresNothingForAnEmptyLog()
    {
        var result = TetrisEngine.Replay(7, Array.Empty<TetrisEngine.Placement>());

        Assert.True(result.Valid);
        Assert.Equal(0, result.Score);
        Assert.Equal(0, result.Lines);
    }

    [Fact]
    public void TetrisReplay_RejectsALogLongerThanTheCap()
    {
        var log = new TetrisEngine.Placement[TetrisEngine.MaxPlacements + 1];
        Array.Fill(log, new TetrisEngine.Placement(0, 0, 0, false));

        Assert.False(TetrisEngine.Replay(1, log).Valid);
    }

    /// <summary>
    /// A completed row pays the guideline single: 100 points at level one.
    ///
    /// Built from I + J + L, which is the one combination that tiles a ten-wide row with flat
    /// bottoms — 4 + 3 + 3 — and the bag guarantees exactly one of each per seven pieces. The
    /// seed search finds a bag that opens with those three in any order, so no other shape has to
    /// be placed before the row completes.
    /// </summary>
    [Fact]
    public void TetrisReplay_ScoresALineClear()
    {
        var wanted = new[] { TetrisEngine.Piece.I, TetrisEngine.Piece.J, TetrisEngine.Piece.L };

        // Columns each piece fills on the bottom row: I spans 0-3, J spans 4-6, L spans 7-9.
        var column = new Dictionary<TetrisEngine.Piece, int>
        {
            [TetrisEngine.Piece.I] = 0,
            [TetrisEngine.Piece.J] = 4,
            [TetrisEngine.Piece.L] = 7,
        };

        for (uint seed = 1; seed < 20_000; seed++)
        {
            var rng = new DeterministicRng(seed);
            var bag = TetrisEngine.Bag(ref rng);

            if (!bag.Take(3).ToHashSet().SetEquals(wanted)) continue;

            var placements = bag.Take(3)
                .Select(piece => new TetrisEngine.Placement(0, column[piece], 18, false))
                .ToList();

            var result = TetrisEngine.Replay(seed, placements);

            Assert.True(result.Valid, result.Rejection);
            Assert.Equal(1, result.Lines);

            // A single at level one. No combo (the first clear is combo zero), no back-to-back,
            // and not a perfect clear — J and L each leave one cell on the row above.
            Assert.Equal(100, result.Score);
            return;
        }

        Assert.Fail("No seed in range opened with I, J and L in the first three pieces.");
    }

    /// <summary>
    /// A row that is not full pays nothing. Shape-agnostic: in rotation 0 every tetromino has its
    /// lowest cells on the second row of its box, so y = 18 seats any of them on the floor.
    /// </summary>
    [Fact]
    public void TetrisReplay_PaysNothingForAPartialRow()
    {
        var result = TetrisEngine.Replay(12345, new[] { new TetrisEngine.Placement(0, 0, 18, false) });

        Assert.True(result.Valid, result.Rejection);
        Assert.Equal(0, result.Lines);
        Assert.Equal(0, result.Score);
    }

    /// <summary>Same seed, same placements, same score — what the leaderboard rests on.</summary>
    [Fact]
    public void TetrisReplay_IsDeterministic()
    {
        var rng = new DeterministicRng(4711);
        var first = TetrisEngine.Bag(ref rng)[0];
        var floorY = 19 - TetrisEngine.CellsOf(first, 0, 0, 0).Max(c => c.Y);

        var log = new[] { new TetrisEngine.Placement(0, 0, floorY, false) };

        var a = TetrisEngine.Replay(4711, log);
        var b = TetrisEngine.Replay(4711, log);

        Assert.True(a.Valid);
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Lines, b.Lines);
    }

    /// <summary>
    /// Holding on the first placement banks the current piece and plays the next one, so the
    /// piece that lands is genuinely different from the no-hold case.
    /// </summary>
    [Fact]
    public void TetrisReplay_TracksTheHeldPieceAcrossPlacements()
    {
        for (uint seed = 1; seed < 4000; seed++)
        {
            var rng = new DeterministicRng(seed);
            var bag = TetrisEngine.Bag(ref rng);
            if (bag[0] != TetrisEngine.Piece.I || bag[1] != TetrisEngine.Piece.O) continue;

            // The I-piece lies flat across row 19 when placed at y = 18 (its cells sit on the
            // shape's second row), covering columns 0-3.
            var withoutHold = TetrisEngine.Replay(seed, new[] { new TetrisEngine.Placement(0, 0, 18, false) });
            Assert.True(withoutHold.Valid, withoutHold.Rejection);

            // Holding plays the O instead, which occupies columns 0-1 over rows 18-19.
            var withHold = TetrisEngine.Replay(seed, new[] { new TetrisEngine.Placement(0, 0, 18, true) });
            Assert.True(withHold.Valid, withHold.Rejection);

            // Column 2 on row 19 is free after the O but taken after the I — so a placement there
            // is legal only in the hold case, which proves the two diverged.
            var afterHold = TetrisEngine.Replay(seed, new[]
            {
                new TetrisEngine.Placement(0, 0, 18, true),
                new TetrisEngine.Placement(0, 2, 18, false),
            });
            Assert.True(afterHold.Valid, afterHold.Rejection);
            return;
        }

        Assert.Fail("No seed in range opened with I then O.");
    }

    /// <summary>
    /// Cross-language parity for the Tetris bag.
    ///
    /// Tetris is the one game where the browser derives the piece sequence itself, because the
    /// replay checks each placement against the piece *this* bag dealt — a client playing a
    /// different sequence would have every honest game rejected as a forgery.
    ///
    /// These literals were produced by the TypeScript implementation in tetris.ts. They are
    /// pinned here rather than left to integration testing because the failure mode is silent:
    /// JavaScript bitwise operators work on signed 32-bit ints, so an implementation missing one
    /// `>>> 0` agrees with this one for a while and then quietly stops. If this test ever fails,
    /// the two languages have diverged and every Tetris submission is about to be rejected.
    /// </summary>
    [Theory]
    [InlineData(12345u, new[] { 1, 0, 5, 2, 4, 6, 3 }, new[] { 4, 6, 3, 0, 5, 2, 1 })]
    [InlineData(5150u, new[] { 3, 6, 4, 0, 5, 2, 1 }, new[] { 2, 0, 3, 6, 1, 5, 4 })]
    [InlineData(90210u, new[] { 4, 2, 5, 1, 6, 3, 0 }, new[] { 3, 2, 0, 1, 5, 4, 6 })]
    public void TetrisBag_MatchesTheBrowserImplementation(uint seed, int[] first, int[] second)
    {
        var rng = new DeterministicRng(seed);

        Assert.Equal(first, TetrisEngine.Bag(ref rng).Select(p => (int)p));
        Assert.Equal(second, TetrisEngine.Bag(ref rng).Select(p => (int)p));
    }

    /// <summary>
    /// The *schedule* on which bags are drawn, not just their contents.
    ///
    /// This is a separate property from bag parity and a nastier one. Both sides share one
    /// generator, so they must draw bags at the same moments as well as produce the same values.
    /// The queue is seeded with a bag before any piece is taken and topped up with `if`, not
    /// `while` — a `while` fills an empty queue to fourteen where this stops at seven, which
    /// consumes an extra bag up front and shifts every piece from then on. Bag parity still
    /// passes in that world; the game simply desynchronises and rejects honest runs.
    ///
    /// Literals produced by the browser implementation in TetrisGame.tsx.
    /// </summary>
    [Fact]
    public void TetrisPieceSchedule_MatchesTheBrowserImplementation()
    {
        var rng = new DeterministicRng(12345);
        var queue = new Queue<TetrisEngine.Piece>(TetrisEngine.Bag(ref rng));
        var dealt = new List<int>();

        for (var i = 0; i < 15; i++)
        {
            if (queue.Count <= 7) foreach (var piece in TetrisEngine.Bag(ref rng)) queue.Enqueue(piece);
            dealt.Add((int)queue.Dequeue());
        }

        Assert.Equal(new[] { 1, 0, 5, 2, 4, 6, 3, 4, 6, 3, 0, 5, 2, 1, 4 }, dealt);
    }

    // ── Sokoban ───────────────────────────────────────────────────────────

    /// <summary>
    /// Breadth-first search over (player position, box positions).
    ///
    /// BFS rather than anything cleverer because it returns the *shortest* solution, which is
    /// what the par values are checked against — a heuristic search would find some solution and
    /// tell us nothing about whether par is achievable.
    /// </summary>
    private static int? ShortestSolution(SokobanEngine.Level level, int nodeBudget = 400_000)
    {
        var start = SokobanEngine.Parse(level);
        if (SokobanEngine.IsSolved(start)) return 0;

        var seen = new HashSet<string> { SokobanEngine.Signature(start) };
        var queue = new Queue<(SokobanEngine.State State, int Depth)>();
        queue.Enqueue((start, 0));

        var expanded = 0;

        while (queue.Count > 0)
        {
            var (state, depth) = queue.Dequeue();

            if (++expanded > nodeBudget) return null;

            for (var direction = 0; direction < 4; direction++)
            {
                var next = SokobanEngine.Clone(state);
                if (!SokobanEngine.TryMove(next, direction)) continue;

                var key = SokobanEngine.Signature(next);
                if (!seen.Add(key)) continue;

                if (SokobanEngine.IsSolved(next)) return depth + 1;

                queue.Enqueue((next, depth + 1));
            }
        }

        return null;
    }

    /// <summary>
    /// The property that matters most in this whole file.
    ///
    /// An unsolvable Sokoban level does not look broken — it looks hard. A player can lose an
    /// hour to one and conclude they are bad at the game. Proving solvability by search is the
    /// only way to know, and it is why these levels are shipped rather than the copyrighted
    /// classic sets that would have come pre-tested.
    /// </summary>
    [Fact]
    public void SokobanLevels_AreAllSolvable()
    {
        for (var i = 0; i < SokobanEngine.Levels.Length; i++)
        {
            var level = SokobanEngine.Levels[i];
            var shortest = ShortestSolution(level);

            Assert.True(shortest.HasValue, $"Level {i} ('{level.Name}') has no solution.");
            Assert.True(shortest!.Value > 0, $"Level {i} ('{level.Name}') starts already solved.");
        }
    }

    /// <summary>
    /// Par has to be reachable, or the scoring curve is a lie: par pays base points, and a par
    /// nobody can hit means the advertised score is unobtainable. Par is allowed to be generous —
    /// it is a target, not a record — but never tighter than the optimal solution.
    /// </summary>
    [Fact]
    public void SokobanLevels_HaveAchievablePar()
    {
        for (var i = 0; i < SokobanEngine.Levels.Length; i++)
        {
            var level = SokobanEngine.Levels[i];
            var shortest = ShortestSolution(level);

            Assert.True(shortest.HasValue, $"Level {i} ('{level.Name}') has no solution.");
            Assert.True(
                level.ParMoves >= shortest!.Value,
                $"Level {i} ('{level.Name}') sets par at {level.ParMoves} but the optimum is {shortest.Value}.");
        }
    }

    /// <summary>Each level needs exactly one player and at least as many goals as boxes.</summary>
    [Fact]
    public void SokobanLevels_AreWellFormed()
    {
        foreach (var level in SokobanEngine.Levels)
        {
            var players = 0;
            var boxes = 0;
            var goals = 0;

            foreach (var row in level.Rows)
            {
                foreach (var cell in row)
                {
                    if (cell is '@' or '+') players++;
                    if (cell is '$' or '*') boxes++;
                    if (cell is '.' or '*' or '+') goals++;
                }
            }

            Assert.True(players == 1, $"'{level.Name}' has {players} players.");
            Assert.True(boxes > 0, $"'{level.Name}' has no boxes.");
            Assert.True(goals >= boxes, $"'{level.Name}' has {boxes} boxes but only {goals} goals.");
        }
    }

    /// <summary>
    /// A box against a wall it cannot be pushed along is dead, and the engine must simply refuse
    /// the move rather than letting it slide through. Two boxes in a line are the same rule: they
    /// cannot be pushed together, which is most of what makes the game hard.
    /// </summary>
    [Fact]
    public void Sokoban_RefusesImpossiblePushes()
    {
        var level = new SokobanEngine.Level("Test", new[]
        {
            "#####",
            "#@$$#",
            "#####",
        }, 1);

        var state = SokobanEngine.Parse(level);

        // Right pushes a box into a box: not allowed.
        Assert.False(SokobanEngine.TryMove(state, 1));

        // Left walks into a wall.
        Assert.False(SokobanEngine.TryMove(state, 3));
    }

    [Fact]
    public void Sokoban_PushesABoxOntoAGoal()
    {
        var level = new SokobanEngine.Level("Test", new[]
        {
            "#####",
            "#@$.#",
            "#####",
        }, 1);

        var state = SokobanEngine.Parse(level);

        Assert.False(SokobanEngine.IsSolved(state));
        Assert.True(SokobanEngine.TryMove(state, 1));
        Assert.True(SokobanEngine.IsSolved(state));
    }

    [Fact]
    public void SokobanReplay_AcceptsASolutionAndRejectsAnUnfinishedOne()
    {
        // Level 0 is the tutorial: one push right puts the only box on the only goal.
        var solved = SokobanEngine.Replay(0, new[] { 1 });
        Assert.True(solved.Valid, solved.Rejection);
        Assert.True(solved.Solved);
        Assert.Equal(1, solved.Moves);

        // Walking up is a legal move that solves nothing.
        var partial = SokobanEngine.Replay(0, new[] { 0 });
        Assert.True(partial.Valid, partial.Rejection);
        Assert.False(partial.Solved);

        // And pushing past the goal un-solves it, which the engine must report honestly rather
        // than latching "solved" the first time every box happens to be home.
        var overshot = SokobanEngine.Replay(0, new[] { 1, 1 });
        Assert.True(overshot.Valid, overshot.Rejection);
        Assert.False(overshot.Solved);
    }

    /// <summary>An honest client never sends a move it could not make.</summary>
    [Fact]
    public void SokobanReplay_RejectsAnImpossibleMove()
    {
        // Three pushes right drives the box into the far wall; the third cannot happen.
        var result = SokobanEngine.Replay(0, new[] { 1, 1, 1 });

        Assert.False(result.Valid);
        Assert.NotNull(result.Rejection);
    }

    [Fact]
    public void SokobanReplay_RejectsUnknownLevelsAndOverlongLogs()
    {
        Assert.False(SokobanEngine.Replay(-1, new[] { 1 }).Valid);
        Assert.False(SokobanEngine.Replay(999, new[] { 1 }).Valid);
        Assert.False(SokobanEngine.Replay(0, new int[SokobanEngine.MaxMoves + 1]).Valid);
    }

    [Fact]
    public void SokobanReplay_IsDeterministic()
    {
        var a = SokobanEngine.Replay(0, new[] { 1, 1 });
        var b = SokobanEngine.Replay(0, new[] { 1, 1 });

        Assert.Equal(a.Solved, b.Solved);
        Assert.Equal(a.Moves, b.Moves);
    }
}
