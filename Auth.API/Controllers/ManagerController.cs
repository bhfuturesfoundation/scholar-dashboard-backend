using Auth.Models.DTOs;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers
{
    [Route("api/manager")]
    [ApiController]
    [Authorize]
    public class ManagerController : ControllerBase
    {
        private readonly IManagerService _managerService;
        private readonly ILogger<ManagerController> _logger;

        public ManagerController(IManagerService managerService,ILogger<ManagerController> logger)
        {
            _managerService = managerService;
            _logger = logger;
        }

        [HttpGet("{scholarId}/{monthYear}")]
        public async Task<ActionResult<ApiResponse<List<JournalAnswerResponse>>>> GetJournalForUser(string scholarId, string monthYear)
        {
            if (string.IsNullOrWhiteSpace(scholarId) || string.IsNullOrWhiteSpace(monthYear))
                return BadRequest(ApiResponse<List<JournalAnswerResponse>>.ErrorResponse("ScholarId and MonthYear are required"));

            try
            {
                var data = await _managerService.GetJournalForUserAsync(scholarId, monthYear);

                if (data == null || !data.Any())
                {
                    _logger.LogWarning("No journal entries found for ScholarId: {ScholarId}, MonthYear: {MonthYear}", scholarId, monthYear);
                    return NotFound(ApiResponse<List<JournalAnswerResponse>>.ErrorResponse("No journal entries found for this user and month"));
                }

                return Ok(ApiResponse<List<JournalAnswerResponse>>.SuccessResponse(data, "Journal entries fetched successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching journal entries for ScholarId: {ScholarId}, MonthYear: {MonthYear}", scholarId, monthYear);
                return StatusCode(500, ApiResponse<List<JournalAnswerResponse>>.ErrorResponse("An unexpected error occurred while fetching the journal"));
            }
        }
        [HttpGet("overview")]
        public async Task<ActionResult<ApiResponse<List<ScholarJournalOverviewDto>>>> GetJournalOverview()
        {
            try
            {
                var data = await _managerService.GetJournalOverviewAsync();

                if (data == null || !data.Any())
                {
                    _logger.LogWarning("No journal overview entries found");
                    return NotFound(ApiResponse<List<ScholarJournalOverviewDto>>.ErrorResponse("No journal overview entries found"));
                }

                return Ok(ApiResponse<List<ScholarJournalOverviewDto>>.SuccessResponse(data, "Journal overview fetched successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching journal overview");
                return StatusCode(500, ApiResponse<List<ScholarJournalOverviewDto>>.ErrorResponse("An unexpected error occurred while fetching the journal overview"));
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
        [HttpGet("{userId}/submissions")]
        public async Task<ActionResult<ApiResponse<List<JournalSubmissionStatusDto>>>> GetUserSubmissions(string userId)
        {
            try
            {
                var submissions = await _managerService.GetUserSubmissionsAsync(userId);

                if (submissions == null || !submissions.Any())
                {
                    _logger.LogWarning("No submissions found for user {UserId}", userId);
                    return NotFound(ApiResponse<List<JournalSubmissionStatusDto>>.ErrorResponse($"No submissions found for user {userId}"));
                }

                return Ok(ApiResponse<List<JournalSubmissionStatusDto>>.SuccessResponse(submissions, "User submissions fetched successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching submissions for user {UserId}", userId);
                return StatusCode(500, ApiResponse<List<JournalSubmissionStatusDto>>.ErrorResponse("An unexpected error occurred while fetching submissions"));
            }
        }
    }
}
