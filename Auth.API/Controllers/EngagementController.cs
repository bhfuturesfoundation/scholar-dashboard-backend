using Auth.Models.Constants;
using Auth.Models.DTOs.Engagement;
using Auth.Models.Response;
using Auth.Services.Interfaces.Engagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers
{
    /// <summary>
    /// A scholar's own progress, badges and peer recognition.
    ///
    /// Every read is scoped to the caller's own id taken from the token, never from a route
    /// parameter. Journal satisfaction ratings are personal, and an endpoint that accepted a
    /// scholar id would let any signed-in user read anyone else's.
    /// </summary>
    [Route("api/engagement")]
    [ApiController]
    [Authorize]
    public class EngagementController : ControllerBase
    {
        private readonly IScholarProgressService _progress;
        private readonly IKudosService _kudos;
        private readonly ILogger<EngagementController> _logger;

        public EngagementController(
            IScholarProgressService progress,
            IKudosService kudos,
            ILogger<EngagementController> logger)
        {
            _progress = progress;
            _kudos = kudos;
            _logger = logger;
        }

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        // ── Progress ──────────────────────────────────────────────────────────

        /// <summary>The caller's journal trend, streak, cohort comparison and badges.</summary>
        [HttpGet("progress")]
        public async Task<ActionResult<ApiResponse<ScholarProgressDto>>> GetProgress(CancellationToken ct)
        {
            // Evaluated on read so a scholar who has just qualified sees the badge
            // immediately, rather than only after some other action happens to trigger it.
            // Idempotent, so repeated loads award nothing extra.
            await _progress.EvaluateAsync(UserId, ct);

            var progress = await _progress.GetProgressAsync(UserId, ct);
            return Ok(ApiResponse<ScholarProgressDto>.SuccessResponse(progress, "Progress retrieved"));
        }

        [HttpGet("achievements")]
        public async Task<ActionResult<ApiResponse<List<AchievementDto>>>> GetAchievements(CancellationToken ct) =>
            Ok(ApiResponse<List<AchievementDto>>.SuccessResponse(
                await _progress.GetAchievementsAsync(UserId, ct), "Achievements retrieved"));

        [HttpPost("achievements/seen")]
        public async Task<ActionResult<ApiResponse<bool>>> MarkSeen(CancellationToken ct)
        {
            await _progress.MarkAchievementsSeenAsync(UserId, ct);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Acknowledged"));
        }

        // ── Kudos ─────────────────────────────────────────────────────────────

        [HttpGet("kudos/categories")]
        public ActionResult<ApiResponse<List<KudosCategoryDto>>> GetCategories() =>
            Ok(ApiResponse<List<KudosCategoryDto>>.SuccessResponse(_kudos.GetCategories(), "Categories retrieved"));

        [HttpGet("kudos")]
        public async Task<ActionResult<ApiResponse<KudosSummaryDto>>> GetMyKudos(CancellationToken ct) =>
            Ok(ApiResponse<KudosSummaryDto>.SuccessResponse(
                await _kudos.GetForUserAsync(UserId, ct), "Kudos retrieved"));

        /// <summary>Recent recognition across the cohort — the shared feed.</summary>
        [HttpGet("kudos/recent")]
        public async Task<ActionResult<ApiResponse<List<KudosDto>>>> GetRecent(
            [FromQuery] int limit = 20, CancellationToken ct = default) =>
            Ok(ApiResponse<List<KudosDto>>.SuccessResponse(
                await _kudos.GetRecentAsync(limit, ct), "Recent kudos retrieved"));

        [HttpPost("kudos")]
        public async Task<ActionResult<ApiResponse<KudosDto>>> GiveKudos(
            [FromBody] GiveKudosRequest request, CancellationToken ct)
        {
            // Sender is always the authenticated caller — never taken from the body, or one
            // scholar could post recognition as another.
            var kudos = await _kudos.GiveAsync(UserId, request.ToUserId, request.Category, request.Message, ct);
            return Ok(ApiResponse<KudosDto>.SuccessResponse(kudos, "Kudos sent"));
        }

        /// <summary>Staff moderation.</summary>
        [Authorize(Roles = AppRoles.JournalOversight)]
        [HttpPost("kudos/{id:int}/hide")]
        public async Task<ActionResult<ApiResponse<bool>>> HideKudos(int id, CancellationToken ct)
        {
            await _kudos.HideAsync(id, ct);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Kudos hidden"));
        }
    }

    public class GiveKudosRequest
    {
        public string ToUserId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string? Message { get; set; }
    }
}
