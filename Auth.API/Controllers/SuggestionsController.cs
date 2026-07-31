using System.Security.Claims;
using Auth.Models.Constants;
using Auth.Models.DTOs.Suggestions;
using Auth.Models.Response;
using Auth.Services.Interfaces.Suggestions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers
{
    /// <summary>
    /// The suggestion board.
    ///
    /// Reads and posts are open to any signed-in account; setting a status or hiding a note
    /// is staff-only. Every write is attributed to the token's user id, never to a body
    /// field, so a caller cannot post or vote as someone else.
    /// </summary>
    [Route("api/suggestions")]
    [ApiController]
    [Authorize]
    public class SuggestionsController : ControllerBase
    {
        private readonly ISuggestionService _suggestions;
        private readonly ILogger<SuggestionsController> _logger;

        public SuggestionsController(
            ISuggestionService suggestions,
            ILogger<SuggestionsController> logger)
        {
            _suggestions = suggestions;
            _logger = logger;
        }

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        private string DisplayName
        {
            get
            {
                var first = User.FindFirstValue("FirstName");
                var last = User.FindFirstValue("LastName");
                var name = $"{first} {last}".Trim();

                return string.IsNullOrWhiteSpace(name)
                    ? User.FindFirstValue(ClaimTypes.Email) ?? "A scholar"
                    : name;
            }
        }

        /// <summary>Admin and Program Manager may triage the board.</summary>
        private bool CanModerate =>
            User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.ProgramManager);

        [HttpGet]
        public async Task<ActionResult<ApiResponse<SuggestionBoardDto>>> GetBoard(CancellationToken ct) =>
            Ok(ApiResponse<SuggestionBoardDto>.SuccessResponse(
                await _suggestions.GetBoardAsync(UserId, CanModerate, ct), "Board retrieved"));

        [HttpPost]
        public async Task<ActionResult<ApiResponse<SuggestionDto>>> Create(
            [FromBody] CreateSuggestionRequest request, CancellationToken ct)
        {
            var created = await _suggestions.CreateAsync(UserId, DisplayName, request, ct);
            return Ok(ApiResponse<SuggestionDto>.SuccessResponse(created, "Suggestion posted"));
        }

        [HttpPost("{id:int}/vote")]
        public async Task<ActionResult<ApiResponse<SuggestionDto>>> ToggleVote(int id, CancellationToken ct) =>
            Ok(ApiResponse<SuggestionDto>.SuccessResponse(
                await _suggestions.ToggleVoteAsync(UserId, id, ct), "Vote updated"));

        [HttpDelete("{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(int id, CancellationToken ct) =>
            Ok(ApiResponse<bool>.SuccessResponse(
                await _suggestions.DeleteAsync(UserId, id, CanModerate, ct), "Suggestion removed"));

        // ── Moderation ────────────────────────────────────────────────────────

        [HttpPut("{id:int}/status")]
        [Authorize(Roles = AppRoles.JournalOversight)]
        public async Task<ActionResult<ApiResponse<SuggestionDto>>> SetStatus(
            int id, [FromBody] UpdateSuggestionStatusRequest request, CancellationToken ct)
        {
            var updated = await _suggestions.SetStatusAsync(id, request, DisplayName, ct);

            _logger.LogInformation(
                "Suggestion {Id} status set to {Status} by {User}.", id, request.Status, UserId);

            return Ok(ApiResponse<SuggestionDto>.SuccessResponse(updated, "Status updated"));
        }

        [HttpPut("{id:int}/hidden")]
        [Authorize(Roles = AppRoles.JournalOversight)]
        public async Task<ActionResult<ApiResponse<bool>>> SetHidden(
            int id, [FromQuery] bool hidden, CancellationToken ct) =>
            Ok(ApiResponse<bool>.SuccessResponse(
                await _suggestions.SetHiddenAsync(id, hidden, ct),
                hidden ? "Suggestion hidden" : "Suggestion restored"));
    }
}
