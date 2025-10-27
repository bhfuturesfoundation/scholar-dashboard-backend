using Auth.Models.Entities;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Auth.API.Controllers
{
    [ApiController]
    [Route("api/answers")]
    [Authorize]
    public class AnswerController : ControllerBase
    {
        private readonly IAnswerService _answerService;
        private readonly ILogger<AnswerController> _logger;

        public AnswerController(IAnswerService answerService, ILogger<AnswerController> logger)
        {
            _answerService = answerService;
            _logger = logger;
        }

        /// <summary>
        /// Get all answers for the current user for a given month (e.g., "2025-08").
        /// </summary>
        [HttpGet("{monthYear}")]
        public async Task<IActionResult> GetAnswersForMonth(string monthYear)
        {
            var scholarId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (scholarId == null)
            {
                _logger.LogWarning("Unauthorized access attempt to get answers for month {MonthYear}", monthYear);
                return Unauthorized(ApiResponse<object>.ErrorResponse("User not authenticated"));
            }

            try
            {
                var answers = await _answerService.GetAnswersForMonthAsync(scholarId, monthYear);
                _logger.LogInformation("Retrieved {Count} answers for scholar {ScholarId} for month {MonthYear}",
                    answers.Count(), scholarId, monthYear);

                return Ok(ApiResponse<IEnumerable<Answer>>.SuccessResponse(
                    answers,
                    $"Retrieved {answers.Count()} answers for {monthYear}"
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving answers for scholar {ScholarId} for month {MonthYear}", scholarId, monthYear);
                return StatusCode(500, ApiResponse<object>.ErrorResponse("Failed to retrieve answers"));
            }
        }

        /// <summary>
        /// Submit answers for the current user (allowed only between 1st and 7th of the month).
        /// </summary>
        [HttpPost("{monthYear}")]
        public async Task<IActionResult> SubmitAnswers(string monthYear, [FromBody] IEnumerable<Answer> answers)
        {
            var scholarId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (scholarId == null)
            {
                _logger.LogWarning("Unauthorized submission attempt for month {MonthYear}", monthYear);
                return Unauthorized(ApiResponse<object>.ErrorResponse("User not authenticated"));
            }

            try
            {
                await _answerService.SubmitAnswersAsync(scholarId, monthYear, answers);
                _logger.LogInformation("Scholar {ScholarId} submitted {Count} answers for month {MonthYear}",
                    scholarId, answers.Count(), monthYear);

                return Ok(ApiResponse<object>.SuccessResponse(
                    null,
                    $"Successfully submitted answers for {monthYear}"
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting answers for scholar {ScholarId} for month {MonthYear}", scholarId, monthYear);
                return StatusCode(500, ApiResponse<object>.ErrorResponse("Failed to submit answers"));
            }
        }
    }
}
