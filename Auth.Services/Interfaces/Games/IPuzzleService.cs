using Auth.Models.Entities.Games;

namespace Auth.Services.Interfaces.Games
{
    public interface IPuzzleService
    {
        /// <summary>Deals a new puzzle and returns everything the client is allowed to see.</summary>
        PuzzleDeal Deal(string userId, string gameId, int difficulty);

        /// <summary>
        /// Minesweeper: the board, derived from the ticket's seed and the opening click.
        /// Returns null when the ticket does not verify.
        /// </summary>
        MinesweeperBoard? OpenBoard(string userId, string ticket, int firstClick);

        /// <summary>
        /// Sudoku: the correct digit for one cell. Returns null when the ticket does not verify.
        /// Every call is one the player pays for at submission time.
        /// </summary>
        SudokuHint? Hint(string userId, string ticket, int cell);

        /// <summary>
        /// Replays a submitted game, scores it, and records the result when it holds up.
        /// Never trusts a number from the caller.
        /// </summary>
        Task<PuzzleOutcome> SubmitAsync(string userId, PuzzleSubmission submission, CancellationToken cancellationToken = default);
    }
}
