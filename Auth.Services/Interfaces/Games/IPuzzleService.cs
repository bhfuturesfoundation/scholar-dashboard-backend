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

        /// <summary>Stores or replaces the in-progress game for this user and puzzle.</summary>
        Task SaveAsync(string userId, string gameId, string ticket, string state, CancellationToken cancellationToken = default);

        /// <summary>The saved game, or null when there is none or it can no longer be scored.</summary>
        Task<PuzzleSaveDto?> LoadSaveAsync(string userId, string gameId, CancellationToken cancellationToken = default);

        /// <summary>Drops the save. Called when a game finishes or is abandoned.</summary>
        Task ClearSaveAsync(string userId, string gameId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Replays a submitted game, scores it, and records the result when it holds up.
        /// Never trusts a number from the caller.
        /// </summary>
        Task<PuzzleOutcome> SubmitAsync(string userId, PuzzleSubmission submission, CancellationToken cancellationToken = default);
    }
}
