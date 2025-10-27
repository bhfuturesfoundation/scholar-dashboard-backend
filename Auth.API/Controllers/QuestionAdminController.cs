using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers
{
    [ApiController]
    [Route("api/admin/questions")]
    [Authorize(Roles = "Admin")]
    public class QuestionAdminController : ControllerBase
    {
        private readonly IQuestionService _questionService;
        private readonly ILogger<QuestionAdminController> _logger;

        public QuestionAdminController(
            IQuestionService questionService,
            ILogger<QuestionAdminController> logger)
        {
            _questionService = questionService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> CreateQuestion([FromBody] Question question)
        {
            try
            {
                var created = await _questionService.CreateQuestionAsync(question);
                _logger.LogInformation("Question {QuestionId} created successfully", created.QuestionId);
                return Ok(ApiResponse<Question>.SuccessResponse(created, "Question successfully created"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating question");
                return StatusCode(500, ApiResponse<Question>.ErrorResponse("Failed to create question"));
            }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateQuestion(int id, [FromBody] Question updated)
        {
            try
            {
                await _questionService.UpdateQuestionAsync(id, updated);
                _logger.LogInformation("Question {QuestionId} updated successfully", id);
                return Ok(ApiResponse<object>.SuccessResponse(null, $"Question {id} updated successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating question {QuestionId}", id);
                return StatusCode(500, ApiResponse<object>.ErrorResponse($"Failed to update question {id}"));
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeactivateQuestion(int id)
        {
            try
            {
                await _questionService.DeactivateQuestionAsync(id);
                return Ok(ApiResponse<object>.SuccessResponse(null, $"Question {id} deactivated successfully"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating question {QuestionId}", id);
                return StatusCode(500, ApiResponse<object>.ErrorResponse($"Failed to deactivate question {id}"));
            }
        }
        [HttpDelete("{id:int}/hard")]
        public async Task<IActionResult> DeleteQuestion(int id)
        {
            try
            {
                await _questionService.DeleteQuestionAsync(id);
                _logger.LogInformation("Question {QuestionId} deleted permanently", id);
                return Ok(ApiResponse<object>.SuccessResponse(null, $"Question {id} deleted permanently"));
            }
            catch (NotFoundException nfEx)
            {
                _logger.LogWarning(nfEx, "Question {QuestionId} not found for deletion", id);
                return NotFound(ApiResponse<object>.ErrorResponse(nfEx.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting question {QuestionId}", id);
                return StatusCode(500, ApiResponse<object>.ErrorResponse($"Failed to delete question {id}"));
            }
        }
        [HttpPut("{id:int}/reactivate")]
        public async Task<IActionResult> ReactivateQuestion(int id)
        {
            try
            {
                await _questionService.ReactivateQuestionAsync(id);
                _logger.LogInformation("Question {QuestionId} reactivated successfully", id);
                return Ok(ApiResponse<object>.SuccessResponse(null, $"Question {id} reactivated successfully"));
            }
            catch (NotFoundException nfEx)
            {
                _logger.LogWarning(nfEx, "Question {QuestionId} not found for reactivation", id);
                return NotFound(ApiResponse<object>.ErrorResponse(nfEx.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reactivating question {QuestionId}", id);
                return StatusCode(500, ApiResponse<object>.ErrorResponse($"Failed to reactivate question {id}"));
            }
        }


    }
}
