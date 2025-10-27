using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers
{
    [ApiController]
    [Route("api/questions")]
    [Authorize]
    public class QuestionController : ControllerBase
    {
        private readonly IQuestionService _questionService;
        private readonly ILogger<QuestionController> _logger;

        public QuestionController(IQuestionService questionService, ILogger<QuestionController> logger)
        {
            _questionService = questionService;
            _logger = logger;
        }

        [HttpGet("active")]
        public async Task<IActionResult> GetActiveQuestions()
        {
            try
            {
                var questions = await _questionService.GetActiveQuestionsAsync();
                _logger.LogInformation("Fetched {Count} active questions", questions.Count());

                return Ok(ApiResponse<object>.SuccessResponse(questions, "Fetched active questions"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching active questions");
                return StatusCode(500, ApiResponse<object>.ErrorResponse("Failed to fetch active questions"));
            }
        }
        [HttpGet("inactive")]
        public async Task<IActionResult> GetInactiveQuestions()
        {
            try
            {
                var questions = await _questionService.GetInactiveQuestionsAsync();
                _logger.LogInformation("Fetched {Count} inactive questions", questions.Count());

                return Ok(ApiResponse<object>.SuccessResponse(questions, "Fetched inactive questions"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching inactive questions");
                return StatusCode(500, ApiResponse<object>.ErrorResponse("Failed to fetch inactive questions"));
            }
        }
    }
}
