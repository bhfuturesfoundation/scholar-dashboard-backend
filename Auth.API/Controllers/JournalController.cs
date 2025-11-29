using Auth.Models.DTOs;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Models.Request;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Auth.Services.Services;
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
        private readonly IAnswerService _answerService;
        private readonly ILogger<JournalController> _logger;

        public JournalController(IJournalService journalService, IAnswerService answerService, ILogger<JournalController> logger)
        {
            _journalService = journalService;
            _answerService = answerService;
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

        [HttpPost("{monthYear}/draft")]
        public async Task<IActionResult> SaveDraft(string monthYear, [FromBody] IEnumerable<SaveDraftAnswerDto> answers)
        {
            var scholarId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (scholarId == null)
            {
                _logger.LogWarning("Unauthorized draft save attempt for {MonthYear}", monthYear);
                return Unauthorized(ApiResponse<object>.ErrorResponse("User not authenticated"));
            }

            try
            {
                await _answerService.SaveDraftAsync(scholarId, monthYear, answers);
                _logger.LogInformation("Draft saved for scholar {ScholarId} for {MonthYear}", scholarId, monthYear);

                return Ok(ApiResponse<object>.SuccessResponse(
                    null,
                    $"Draft saved successfully for {monthYear}"
                ));
            }
            catch (ConflictException ex)
            {
                _logger.LogWarning(ex, "Conflict while saving draft for {ScholarId} for {MonthYear}", scholarId, monthYear);
                return Conflict(ApiResponse<object>.ErrorResponse(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving draft for scholar {ScholarId} for {MonthYear}", scholarId, monthYear);
                return StatusCode(500, ApiResponse<object>.ErrorResponse("Failed to save draft"));
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
