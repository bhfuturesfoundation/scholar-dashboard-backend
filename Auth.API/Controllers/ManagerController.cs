using Auth.Models.Constants;
using Auth.Models.DTOs;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers
{
    /// <summary>
    /// Program-manager view over scholar journals.
    ///
    /// SECURITY: this controller was previously annotated with a bare <c>[Authorize]</c>,
    /// so ANY authenticated account — an ordinary scholar, a mentor, even an FLS speaker —
    /// could read every scholar's journal answers and personal details by calling
    /// <c>/api/manager/overview</c> or <c>/api/manager/{userId}/{monthYear}</c> directly.
    /// Access is now restricted to the roles that are actually meant to have oversight.
    /// </summary>
    [Route("api/manager")]
    [ApiController]
    [Authorize(Roles = AppRoles.JournalOversight)]
    public class ManagerController : ControllerBase
    {
        private readonly IManagerService _managerService;
        private readonly ILogger<ManagerController> _logger;

        public ManagerController(IManagerService managerService, ILogger<ManagerController> logger)
        {
            _managerService = managerService;
            _logger = logger;
        }

        /// <summary>
        /// Journal answers for one scholar and month.
        ///
        /// Returns 200 with an empty list when the scholar hasn't written anything yet.
        /// This used to 404, which the frontend's fetch wrapper turns into a thrown error —
        /// and because the detail page loads the journal, the profile and the submission
        /// grid in a single <c>Promise.all</c>, one empty month blanked the entire page.
        /// "No data" is a valid answer, not a failure.
        /// </summary>
        [HttpGet("{scholarId}/{monthYear}")]
        public async Task<ActionResult<ApiResponse<List<JournalAnswerResponse>>>> GetJournalForUser(
            string scholarId, string monthYear)
        {
            if (string.IsNullOrWhiteSpace(scholarId) || string.IsNullOrWhiteSpace(monthYear))
                return BadRequest(ApiResponse<List<JournalAnswerResponse>>.ErrorResponse("ScholarId and MonthYear are required"));

            try
            {
                var data = await _managerService.GetJournalForUserAsync(scholarId, monthYear);

                return Ok(ApiResponse<List<JournalAnswerResponse>>.SuccessResponse(
                    data ?? new List<JournalAnswerResponse>(),
                    data is { Count: > 0 }
                        ? "Journal entries fetched successfully"
                        : "No journal entries for this month"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching journal entries for ScholarId: {ScholarId}, MonthYear: {MonthYear}", scholarId, monthYear);
                return StatusCode(500, ApiResponse<List<JournalAnswerResponse>>.ErrorResponse("An unexpected error occurred while fetching the journal"));
            }
        }

        [HttpGet("overview")]
        public async Task<ActionResult<ApiResponse<PagedResult<ScholarJournalOverviewDto>>>> GetJournalOverview(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100)
        {
            try
            {
                var result = await _managerService.GetJournalOverviewAsync(page, pageSize);
                return Ok(ApiResponse<PagedResult<ScholarJournalOverviewDto>>.SuccessResponse(result, "Journal overview fetched successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching journal overview");
                return StatusCode(500, ApiResponse<PagedResult<ScholarJournalOverviewDto>>.ErrorResponse("An unexpected error occurred while fetching the journal overview"));
            }
        }

        [HttpGet("{userId}")]
        public async Task<ActionResult<ApiResponse<UserDetailsResponse>>> GetUserById(string userId)
        {
            try
            {
                var user = await _managerService.GetUserByIdAsync(userId);

                if (user == null)
                {
                    _logger.LogWarning("User with ID {UserId} not found", userId);
                    return NotFound(ApiResponse<UserDetailsResponse>.ErrorResponse($"User with ID {userId} not found"));
                }

                return Ok(ApiResponse<UserDetailsResponse>.SuccessResponse(user, "User details fetched successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user with ID {UserId}", userId);
                return StatusCode(500, ApiResponse<UserDetailsResponse>.ErrorResponse("An unexpected error occurred while fetching the user details"));
            }
        }

        /// <summary>
        /// Monthly submission flags for one scholar. Like the journal endpoint above, an
        /// empty result is a 200 with an empty list — a scholar who has never submitted is
        /// a normal state, not a missing resource.
        /// </summary>
        [HttpGet("{userId}/submissions")]
        public async Task<ActionResult<ApiResponse<List<JournalSubmissionStatusDto>>>> GetUserSubmissions(string userId)
        {
            try
            {
                var submissions = await _managerService.GetUserSubmissionsAsync(userId);

                return Ok(ApiResponse<List<JournalSubmissionStatusDto>>.SuccessResponse(
                    submissions ?? new List<JournalSubmissionStatusDto>(),
                    submissions is { Count: > 0 }
                        ? "User submissions fetched successfully"
                        : "No submissions recorded for this scholar"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching submissions for user {UserId}", userId);
                return StatusCode(500, ApiResponse<List<JournalSubmissionStatusDto>>.ErrorResponse("An unexpected error occurred while fetching submissions"));
            }
        }
    }
}
