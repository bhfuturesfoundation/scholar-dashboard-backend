using System.Security.Claims;
using Auth.Models.Entities.Games;
using Auth.Models.Response;
using Auth.Services.Interfaces.Games;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Auth.API.Controllers
{
    /// <summary>
    /// Sudoku, 2048 and Minesweeper.
    ///
    /// Two endpoints, because these games do not need more. The client asks for a board, plays it
    /// entirely offline — no round trip per move, so it stays responsive on a bad connection and
    /// works with the tab in the background — and posts the finished game to be replayed and
    /// scored. Compare Comet Arena, which needs a live hub because its simulation advances
    /// whether or not anyone is looking; nothing here does.
    /// </summary>
    [Route("api/puzzles")]
    [ApiController]
    [Authorize]
    public class PuzzlesController : ControllerBase
    {
        private readonly IPuzzleService _puzzles;

        public PuzzlesController(IPuzzleService puzzles)
        {
            _puzzles = puzzles;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        [HttpPost("{gameId}/deal")]
        [EnableRateLimiting("puzzle-deal")]
        public ActionResult<ApiResponse<PuzzleDeal>> Deal(string gameId, [FromQuery] int difficulty = 0)
        {
            if (!PuzzleGames.IsKnown(gameId))
                return NotFound(ApiResponse<PuzzleDeal>.ErrorResponse($"There is no puzzle called '{gameId}'."));

            var deal = _puzzles.Deal(GetUserId(), gameId, difficulty);
            return Ok(ApiResponse<PuzzleDeal>.SuccessResponse(deal, "Dealt."));
        }

        /// <summary>
        /// Minesweeper's opening click, which is also when the board comes into existence.
        ///
        /// Not rate-limited under the deal policy: this is cheap (no uniqueness search, just a
        /// shuffle) and it is called once per game, immediately after a deal that was limited.
        /// </summary>
        [HttpPost("minesweeper/open")]
        public ActionResult<ApiResponse<MinesweeperBoard>> OpenBoard(
            [FromBody] OpenBoardRequest request)
        {
            var board = _puzzles.OpenBoard(GetUserId(), request.Ticket, request.FirstClick);

            return board is null
                ? BadRequest(ApiResponse<MinesweeperBoard>.ErrorResponse("That game could not be verified."))
                : Ok(ApiResponse<MinesweeperBoard>.SuccessResponse(board, "Opened."));
        }

        [HttpPost("sudoku/hint")]
        public ActionResult<ApiResponse<SudokuHint>> Hint([FromBody] HintRequest request)
        {
            var hint = _puzzles.Hint(GetUserId(), request.Ticket, request.Cell);

            return hint is null
                ? BadRequest(ApiResponse<SudokuHint>.ErrorResponse("That game could not be verified."))
                : Ok(ApiResponse<SudokuHint>.SuccessResponse(hint, "Hint."));
        }

        // ── Saves ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Stores the game in progress.
        ///
        /// Called on an interval and on unload, so it has to be cheap and it has to be safe to
        /// call with nothing having changed. Returns 204 either way — the client has nothing to
        /// do with a response, and a failed autosave is not something to interrupt a game over.
        /// </summary>
        [HttpPut("{gameId}/save")]
        public async Task<IActionResult> Save(string gameId, [FromBody] SaveRequest request, CancellationToken cancellationToken)
        {
            await _puzzles.SaveAsync(GetUserId(), gameId, request.Ticket, request.State ?? string.Empty, cancellationToken);
            return NoContent();
        }

        [HttpGet("{gameId}/save")]
        public async Task<ActionResult<ApiResponse<PuzzleSaveDto?>>> LoadSave(string gameId, CancellationToken cancellationToken)
        {
            var save = await _puzzles.LoadSaveAsync(GetUserId(), gameId, cancellationToken);

            // A missing save is the ordinary case, not a 404: the client asks on every open and
            // "no game in progress" is a perfectly good answer.
            return Ok(ApiResponse<PuzzleSaveDto?>.SuccessResponse(save, save is null ? "No saved game." : "Resumed."));
        }

        [HttpDelete("{gameId}/save")]
        public async Task<IActionResult> ClearSave(string gameId, CancellationToken cancellationToken)
        {
            await _puzzles.ClearSaveAsync(GetUserId(), gameId, cancellationToken);
            return NoContent();
        }

        public class SaveRequest
        {
            public string Ticket { get; set; } = string.Empty;

            /// <summary>Client-defined JSON. The server stores it without looking inside.</summary>
            public string? State { get; set; }
        }

        public class OpenBoardRequest
        {
            public string Ticket { get; set; } = string.Empty;
            public int FirstClick { get; set; }
        }

        public class HintRequest
        {
            public string Ticket { get; set; } = string.Empty;
            public int Cell { get; set; }
        }

        /// <summary>
        /// Submits a finished game for replay.
        ///
        /// A rejection is a 200 carrying an unaccepted outcome rather than a 4xx. The distinction
        /// matters to the client: a rejected submission is a *result* to show the player, not a
        /// transport failure to retry — and retrying it would fail identically forever.
        /// </summary>
        [HttpPost("submit")]
        public async Task<ActionResult<ApiResponse<PuzzleOutcome>>> Submit(
            [FromBody] PuzzleSubmission submission,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(submission.Ticket))
                return BadRequest(ApiResponse<PuzzleOutcome>.ErrorResponse("A ticket is required."));

            var outcome = await _puzzles.SubmitAsync(GetUserId(), submission, cancellationToken);

            return Ok(ApiResponse<PuzzleOutcome>.SuccessResponse(
                outcome,
                outcome.Accepted ? "Scored." : outcome.Reason ?? "Not scored."));
        }
    }
}
