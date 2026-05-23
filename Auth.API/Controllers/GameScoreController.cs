using Auth.Models.Request;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers
{
    [Route("api/games")]
    [ApiController]
    [Authorize]
    public class GameScoreController : ControllerBase
    {
        private readonly IGameScoreService _gameScoreService;
        private readonly ILogger<GameScoreController> _logger;

        public GameScoreController(IGameScoreService gameScoreService, ILogger<GameScoreController> logger)
        {
            _gameScoreService = gameScoreService;
            _logger = logger;
        }

        private string GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        /// <summary>
        /// Submit a score for a completed game session.
        /// </summary>
        [HttpPost("scores")]
        public async Task<ActionResult<ApiResponse<bool>>> SubmitScore([FromBody] SubmitScoreRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.GameId))
                return BadRequest(ApiResponse<bool>.ErrorResponse("GameId is required."));

            if (request.Score < 0)
                return BadRequest(ApiResponse<bool>.ErrorResponse("Score cannot be negative."));

            await _gameScoreService.SubmitScoreAsync(GetUserId(), request.GameId, request.Score);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Score submitted."));
        }

        /// <summary>
        /// Get the top-N leaderboard for a specific game.
        /// </summary>
        [HttpGet("scores/leaderboard/{gameId}")]
        public async Task<ActionResult<ApiResponse<List<LeaderboardEntry>>>> GetLeaderboard(
            string gameId,
            [FromQuery] int top = 10)
        {
            top = Math.Clamp(top, 1, 50);
            var entries = await _gameScoreService.GetLeaderboardAsync(gameId, top);
            return Ok(ApiResponse<List<LeaderboardEntry>>.SuccessResponse(entries, $"Top {top} for {gameId}"));
        }

        /// <summary>
        /// Get the calling user's personal best scores across all games.
        /// </summary>
        [HttpGet("scores/my")]
        public async Task<ActionResult<ApiResponse<List<PersonalBest>>>> GetMyBests()
        {
            var bests = await _gameScoreService.GetPersonalBestsAsync(GetUserId());
            return Ok(ApiResponse<List<PersonalBest>>.SuccessResponse(bests, "Personal best scores"));
        }
    }
}
