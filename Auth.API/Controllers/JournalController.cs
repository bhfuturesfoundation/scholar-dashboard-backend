using Auth.Models.Request;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers
{
    [ApiController]
    [Route("api/journal")]
    [Authorize]
    public class JournalController : ControllerBase
    {
        private readonly IJournalService _journalService;
        private readonly ILogger<JournalController> _logger;

        public JournalController(IJournalService journalService, ILogger<JournalController> logger)
        {
            _journalService = journalService;
            _logger = logger;
        }

        [HttpGet("questions")]
        public async Task<IActionResult> GetQuestionsForMonth([FromQuery] string monthYear)
        {
            string scholarId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

            if (string.IsNullOrEmpty(scholarId))
                return Unauthorized(ApiResponse<object>.ErrorResponse("Scholar not found"));

            if (string.IsNullOrEmpty(monthYear))
                return BadRequest(ApiResponse<object>.ErrorResponse("MonthYear query parameter is required"));

            try
            {
                var monthData = await _journalService.GetQuestionsForMonthAsync(scholarId, monthYear);
                return Ok(ApiResponse<object>.SuccessResponse(monthData, "Fetched journal questions"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching journal questions for {Scholar} in {Month}", scholarId, monthYear);
                return StatusCode(500, ApiResponse<object>.ErrorResponse("Failed to fetch journal questions"));
            }
        }

        [HttpPost("answers")]
        public async Task<IActionResult> SubmitAnswers([FromBody] SubmitAnswersRequest request)
        {
            if (request == null || request.Answers == null || !request.Answers.Any())
                return BadRequest(ApiResponse<object>.ErrorResponse("No answers provided"));

            try
            {
                var scholarId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(scholarId)) return Unauthorized();

                request.ScholarId = scholarId; // enforce current scholar

                var result = await _journalService.SubmitAnswersAsync(request);

                if (!result)
                    return BadRequest(ApiResponse<object>.ErrorResponse("Failed to submit answers"));

                return Ok(ApiResponse<object>.SuccessResponse(null, "Answers submitted successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting journal answers for scholar {Scholar}", User.FindFirstValue(ClaimTypes.NameIdentifier));
                return StatusCode(500, ApiResponse<object>.ErrorResponse("Failed to submit answers"));
            }
        }
    }
}
