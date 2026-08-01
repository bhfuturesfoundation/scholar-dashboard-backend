using System.Security.Cryptography;
using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Entities.Games;
using Auth.Services.Interfaces.Games;
using Auth.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Services.Services.Games.Puzzles
{
    /// <inheritdoc cref="IPuzzleService"/>
    public class PuzzleService : IPuzzleService
    {
        private readonly ApplicationDbContext _context;
        private readonly PuzzleTicketSigner _signer;
        private readonly ILogger<PuzzleService> _logger;

        /// <summary>
        /// A 2048 game longer than this is not scored.
        ///
        /// The record human games run to a few thousand moves, so this is generous by roughly an
        /// order of magnitude. It exists because replay cost is linear in the log: without a
        /// bound, one request carrying ten million moves is a free way to occupy a worker thread.
        /// </summary>
        private const int Max2048Moves = 30_000;

        public PuzzleService(
            ApplicationDbContext context,
            IOptions<JWTSettings> jwtSettings,
            ILogger<PuzzleService> logger)
        {
            _context = context;
            _logger = logger;

            // Reuses the configured JWT secret rather than introducing an env var that would be
            // unset on every existing deployment — and unset would mean either a crash on boot or
            // a hardcoded fallback key, which is worse than the coupling. PuzzleTicketSigner
            // derives a distinct key from it, so the two signatures share no bytes.
            _signer = new PuzzleTicketSigner(jwtSettings.Value.Secret);
        }

        // ── Dealing ───────────────────────────────────────────────────────────

        public PuzzleDeal Deal(string userId, string gameId, int difficulty)
        {
            if (!PuzzleGames.IsKnown(gameId))
                throw new ArgumentException($"Unknown puzzle '{gameId}'.", nameof(gameId));

            // Cryptographic, so nobody can precompute a board by guessing when they pressed play.
            // The generator that consumes it is not cryptographic and does not need to be — see
            // DeterministicRng — but the seed itself is the secret.
            var seed = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
            var rng = new DeterministicRng(seed);

            var ticket = new PuzzleTicket
            {
                UserId = userId,
                GameId = gameId,
                Seed = seed,
                Difficulty = difficulty,
                DealtAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)),
            };

            var deal = new PuzzleDeal
            {
                GameId = gameId,
                Difficulty = difficulty,
                Ticket = _signer.Sign(ticket),
            };

            switch (gameId)
            {
                case PuzzleGames.Sudoku:
                {
                    var puzzle = SudokuEngine.Generate(ref rng, ClampSudoku(difficulty));

                    // Only the clues go out. The solution stays here and is regenerated from the
                    // seed at submission time, which is why it never needs storing.
                    deal.Givens = puzzle.Givens;
                    break;
                }

                case PuzzleGames.Game2048:
                {
                    deal.Board = Game2048Engine.Deal(ref rng);
                    break;
                }

                case PuzzleGames.Tetris:
                {
                    // The only field a Tetris client needs, and the only game that gets the seed.
                    deal.Seed = seed;
                    break;
                }

                case PuzzleGames.Sokoban:
                {
                    // The whole level goes out. There is nothing to hide: Sokoban has no hidden
                    // information at all — the puzzle is the grid, and seeing it is the game.
                    var level = SokobanEngine.Levels[ClampSokoban(difficulty)];

                    deal.Rows = level.Rows;
                    deal.Par = level.ParMoves;
                    deal.LevelName = level.Name;
                    deal.LevelCount = SokobanEngine.Levels.Length;
                    break;
                }

                case PuzzleGames.Minesweeper:
                {
                    var spec = MinesweeperEngine.SpecFor(ClampMinesweeper(difficulty));
                    deal.Width = spec.Width;
                    deal.Height = spec.Height;
                    deal.Mines = spec.Mines;

                    // No board is generated yet — the mines depend on where the first click lands.
                    break;
                }
            }

            return deal;
        }

        public MinesweeperBoard? OpenBoard(string userId, string ticketToken, int firstClick)
        {
            var ticket = _signer.Verify(ticketToken, userId);
            if (ticket is null || ticket.GameId != PuzzleGames.Minesweeper) return null;

            var spec = MinesweeperEngine.SpecFor(ClampMinesweeper(ticket.Difficulty));
            if (firstClick < 0 || firstClick >= spec.Cells) return null;

            // Regenerated from the signed seed on every call, which is why nothing was stored.
            // Calling twice with the same first click returns the same board; calling with a
            // different one returns a different board, and the submitted log has to match
            // whichever click it actually opened with.
            var rng = new DeterministicRng(ticket.Seed);
            var board = MinesweeperEngine.Generate(spec, firstClick, ref rng);

            return new MinesweeperBoard
            {
                Width = spec.Width,
                Height = spec.Height,
                Mines = board.Mines,
                Adjacent = board.Adjacent,
            };
        }

        public SudokuHint? Hint(string userId, string ticketToken, int cell)
        {
            var ticket = _signer.Verify(ticketToken, userId);
            if (ticket is null || ticket.GameId != PuzzleGames.Sudoku) return null;
            if (cell < 0 || cell >= SudokuEngine.Cells) return null;

            var rng = new DeterministicRng(ticket.Seed);
            var puzzle = SudokuEngine.Generate(ref rng, ClampSudoku(ticket.Difficulty));

            // Only ever one cell. An endpoint that returned the grid would be a solve button, and
            // the hint count the client reports is not something the server can verify — so the
            // shape of the endpoint is what keeps hints expensive, not the honesty of the caller.
            return new SudokuHint { Cell = cell, Digit = puzzle.Solution[cell] };
        }

        // ── Saves ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A cap on the stored blob.
        ///
        /// The largest honest save is a long Tetris game — a few thousand placements at four
        /// small numbers each, comfortably under 200 KB as JSON. This is not really a correctness
        /// bound, it is a bound on how much a caller can park in the database for free.
        /// </summary>
        private const int MaxStateBytes = 256 * 1024;

        public async Task SaveAsync(
            string userId,
            string gameId,
            string ticket,
            string state,
            CancellationToken cancellationToken = default)
        {
            if (!PuzzleGames.IsKnown(gameId)) return;
            if (state.Length > MaxStateBytes) return;

            // The ticket must be this user's and still valid. Without the check, a save is a way
            // to park arbitrary text under someone else's row.
            if (_signer.Verify(ticket, userId) is null) return;

            var existing = await _context.PuzzleSaves
                .FirstOrDefaultAsync(s => s.UserId == userId && s.GameId == gameId, cancellationToken);

            if (existing is null)
            {
                _context.PuzzleSaves.Add(new PuzzleSave
                {
                    UserId = userId,
                    GameId = gameId,
                    Ticket = ticket,
                    State = state,
                    UpdatedAt = DateTime.UtcNow,
                });
            }
            else
            {
                existing.Ticket = ticket;
                existing.State = state;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Two tabs autosaving at once can race the unique index. Losing one autosave is
                // not worth surfacing — the next one, seconds later, wins.
                _logger.LogDebug("Concurrent puzzle save for {User}/{Game} was dropped.", userId, gameId);
            }
        }

        public async Task<PuzzleSaveDto?> LoadSaveAsync(
            string userId,
            string gameId,
            CancellationToken cancellationToken = default)
        {
            var save = await _context.PuzzleSaves
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId && s.GameId == gameId, cancellationToken);

            if (save is null) return null;

            // A save whose ticket has aged out cannot be scored any more, so offering to resume it
            // would walk the player into a rejection at the end of a long game. Better to drop it
            // and deal them a fresh board.
            var ticket = _signer.Verify(save.Ticket, userId);
            if (ticket is null)
            {
                _context.PuzzleSaves.Remove(new PuzzleSave { Id = save.Id });
                await _context.SaveChangesAsync(cancellationToken);
                return null;
            }

            var dealtAt = DateTimeOffset.FromUnixTimeMilliseconds(ticket.DealtAtUnixMs);

            return new PuzzleSaveDto
            {
                Ticket = save.Ticket,
                State = save.State,
                UpdatedAt = save.UpdatedAt,
                AgeSeconds = (int)Math.Max(0, (DateTimeOffset.UtcNow - dealtAt).TotalSeconds),
            };
        }

        public async Task ClearSaveAsync(string userId, string gameId, CancellationToken cancellationToken = default)
        {
            var save = await _context.PuzzleSaves
                .FirstOrDefaultAsync(s => s.UserId == userId && s.GameId == gameId, cancellationToken);

            if (save is null) return;

            _context.PuzzleSaves.Remove(save);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // ── Scoring ───────────────────────────────────────────────────────────

        public async Task<PuzzleOutcome> SubmitAsync(
            string userId,
            PuzzleSubmission submission,
            CancellationToken cancellationToken = default)
        {
            var ticket = _signer.Verify(submission.Ticket, userId);
            if (ticket is null) return Rejected("This game could not be verified.");

            var seconds = (int)Math.Round(
                (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(ticket.DealtAtUnixMs)).TotalSeconds);

            var outcome = ticket.GameId switch
            {
                PuzzleGames.Sudoku => ScoreSudoku(ticket, submission, seconds),
                PuzzleGames.Game2048 => Score2048(ticket, submission, seconds),
                PuzzleGames.Minesweeper => ScoreMinesweeper(ticket, submission, seconds),
                PuzzleGames.Tetris => ScoreTetris(ticket, submission, seconds),
                PuzzleGames.Sokoban => ScoreSokoban(ticket, submission, seconds),
                _ => Rejected("Unknown puzzle."),
            };

            if (!outcome.Accepted)
            {
                // Worth a log line: a rejection is either a bug in a game client or somebody
                // probing the endpoint, and both are things worth being able to see.
                _logger.LogInformation(
                    "Rejected {Game} submission from {User}: {Reason}",
                    ticket.GameId, userId, outcome.Reason);

                return outcome;
            }

            var previousBest = await _context.GameScores
                .Where(s => s.UserId == userId && s.GameId == ticket.GameId && s.Verified)
                .Select(s => (int?)s.Score)
                .OrderByDescending(s => s)
                .FirstOrDefaultAsync(cancellationToken);

            _context.GameScores.Add(new GameScore
            {
                UserId = userId,
                GameId = ticket.GameId,
                Score = outcome.Score,
                PlayedAt = DateTime.UtcNow,

                // The server replayed this game and did the arithmetic. Nothing here passed
                // through the client as a number, which is what Verified means.
                Verified = true,
                SessionId = ticket.Nonce,
                Mode = ticket.Difficulty,
                DurationSeconds = seconds,
            });

            // The game is over, so its save is dead weight — and worse than dead weight if left,
            // because the next visit would offer to resume a board that has already been scored.
            var save = await _context.PuzzleSaves
                .FirstOrDefaultAsync(s => s.UserId == userId && s.GameId == ticket.GameId, cancellationToken);

            if (save is not null) _context.PuzzleSaves.Remove(save);

            await _context.SaveChangesAsync(cancellationToken);

            outcome.PersonalBest = outcome.Score > (previousBest ?? 0);
            return outcome;
        }

        private static PuzzleOutcome ScoreSudoku(PuzzleTicket ticket, PuzzleSubmission submission, int seconds)
        {
            if (submission.Grid is null || submission.Grid.Length != SudokuEngine.Cells)
                return Rejected("That grid is not the right size.");

            var level = ClampSudoku(ticket.Difficulty);

            // The puzzle is regenerated rather than remembered. Same seed, same generator, same
            // grid — this is the whole reason nothing had to be stored when it was dealt.
            var rng = new DeterministicRng(ticket.Seed);
            var puzzle = SudokuEngine.Generate(ref rng, level);

            if (!SudokuEngine.IsSolved(submission.Grid, puzzle.Solution))
                return Rejected("That grid is not solved.");

            var curve = PuzzleScoring.ForSudoku(level);
            if (seconds < curve.MinimumPlausibleSeconds)
                return Rejected("That finish was too fast to be scored.");

            var hints = Math.Clamp(submission.HintsUsed, 0, SudokuEngine.Cells);
            var score = PuzzleScoring.ApplyHintPenalty(PuzzleScoring.FromTime(curve, seconds), hints);

            return new PuzzleOutcome { Accepted = true, Score = score, Seconds = seconds };
        }

        private static PuzzleOutcome Score2048(PuzzleTicket ticket, PuzzleSubmission submission, int seconds)
        {
            var moves = submission.Moves ?? Array.Empty<int>();

            var replay = Game2048Engine.Replay(ticket.Seed, moves, Max2048Moves);
            if (!replay.Valid) return Rejected(replay.Rejection ?? "That game could not be replayed.");

            // Unlike the timed puzzles this is not a curve — 2048's own score is already the
            // metric the game was designed around, and inventing a second one on top would only
            // make it harder to compare against every other 2048 anyone has played.
            return new PuzzleOutcome
            {
                Accepted = true,
                Score = replay.Score,
                Seconds = seconds,
                HighestTile = replay.HighestTile,
            };
        }

        private static PuzzleOutcome ScoreMinesweeper(PuzzleTicket ticket, PuzzleSubmission submission, int seconds)
        {
            var level = ClampMinesweeper(ticket.Difficulty);
            var reveals = submission.Moves ?? Array.Empty<int>();

            var replay = MinesweeperEngine.Replay(ticket.Seed, level, reveals);
            if (!replay.Valid) return Rejected(replay.Rejection ?? "That game could not be replayed.");
            if (!replay.Cleared) return Rejected("That board was not cleared.");

            var curve = PuzzleScoring.ForMinesweeper(level);
            if (seconds < curve.MinimumPlausibleSeconds)
                return Rejected("That finish was too fast to be scored.");

            return new PuzzleOutcome
            {
                Accepted = true,
                Score = PuzzleScoring.FromTime(curve, seconds),
                Seconds = seconds,
            };
        }

        private static PuzzleOutcome ScoreTetris(PuzzleTicket ticket, PuzzleSubmission submission, int seconds)
        {
            var placements = (submission.Placements ?? Array.Empty<TetrisPlacementDto>())
                .Select(p => new TetrisEngine.Placement(p.Rotation, p.X, p.Y, p.UsedHold))
                .ToList();

            var replay = TetrisEngine.Replay(ticket.Seed, placements);
            if (!replay.Valid) return Rejected(replay.Rejection ?? "That game could not be replayed.");

            // No time curve. Tetris already has a scoring system tuned over forty years — line
            // values, the level multiplier, back-to-back — and layering a second one on top would
            // only make the number incomparable with every other Tetris score anyone has seen.
            return new PuzzleOutcome
            {
                Accepted = true,
                Score = replay.Score,
                Seconds = seconds,
                Lines = replay.Lines,
                Level = replay.Level,
                Tetrises = replay.Tetrises,
            };
        }

        private static PuzzleOutcome ScoreSokoban(PuzzleTicket ticket, PuzzleSubmission submission, int seconds)
        {
            var levelIndex = ClampSokoban(ticket.Difficulty);
            var moves = submission.Moves ?? Array.Empty<int>();

            var replay = SokobanEngine.Replay(levelIndex, moves);
            if (!replay.Valid) return Rejected(replay.Rejection ?? "That solution could not be replayed.");
            if (!replay.Solved) return Rejected("That level is not solved.");

            // Moves, not the clock. There is no minimum-time floor either: a player who has solved
            // a level before genuinely can repeat it in seconds, and penalising that would punish
            // the one thing Sokoban rewards — knowing the answer and executing it cleanly.
            var curve = PuzzleScoring.ForSokoban(SokobanEngine.Levels[levelIndex].ParMoves);

            return new PuzzleOutcome
            {
                Accepted = true,
                Score = PuzzleScoring.FromTime(curve, replay.Moves),
                Seconds = seconds,
                Moves = replay.Moves,
            };
        }

        private static int ClampSokoban(int level) =>
            Math.Clamp(level, 0, SokobanEngine.Levels.Length - 1);

        private static PuzzleOutcome Rejected(string reason) =>
            new() { Accepted = false, Reason = reason };

        private static SudokuEngine.Level ClampSudoku(int difficulty) =>
            (SudokuEngine.Level)Math.Clamp(difficulty, 0, 3);

        private static MinesweeperEngine.Level ClampMinesweeper(int difficulty) =>
            (MinesweeperEngine.Level)Math.Clamp(difficulty, 0, 2);
    }
}
