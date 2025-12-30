using Auth.Models.DTOs;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Models.Request;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers
{
    [Route("api/skills")]
    [ApiController]
    public class SkillController : ControllerBase
    {
        private readonly ISkillService _skillService;
        private readonly ILogger<SkillController> _logger;

        public SkillController(ISkillService skillService, ILogger<SkillController> logger)
        {
            _skillService = skillService;
            _logger = logger;
        }

        /// <summary>
        /// Get all skills for a scholar (active and inactive).
        /// </summary>
        [HttpGet("{scholarId}")]
        public async Task<ActionResult<ApiResponse<IEnumerable<ScholarSkillDto>>>> GetSkills(string scholarId)
        {
            try
            {
                var skills = await _skillService.GetSkillsAsync(scholarId);
                return Ok(ApiResponse<IEnumerable<ScholarSkillDto>>.SuccessResponse(
                    skills,
                    $"Fetched {skills.Count()} skills for scholar {scholarId}"
                ));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch skills for scholar {ScholarId}", scholarId);
                return StatusCode(500, ApiResponse<IEnumerable<ScholarSkillDto>>.ErrorResponse(
                    "Failed to fetch skills"
                ));
            }
        }

        /// <summary>
        /// Add or update a skill for a specific slot.
        /// </summary>
        [HttpPost("{scholarId}/slot/{slot}")]
        public async Task<ActionResult<ApiResponse<ScholarSkillDto>>> AddOrUpdateSkill(
            string scholarId,
            int slot,
            [FromBody] AddSkillRequest request)
        {
            if (request == null)
                return BadRequest(ApiResponse<ScholarSkillDto>.ErrorResponse("Request body cannot be null"));

            if (slot < 1 || slot > 4)
                return BadRequest(ApiResponse<ScholarSkillDto>.ErrorResponse("Slot must be between 1 and 4"));

            try
            {
                var skillDto = await _skillService.AddOrUpdateSkillAsync(scholarId, slot, request.SkillAnswer);
                return Ok(ApiResponse<ScholarSkillDto>.SuccessResponse(skillDto, "Skill saved successfully"));
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning("Failed to save skill for scholar {ScholarId}: {Message}", scholarId, ex.Message);
                return BadRequest(ApiResponse<ScholarSkillDto>.ErrorResponse(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save skill for scholar {ScholarId}", scholarId);
                return StatusCode(500, ApiResponse<ScholarSkillDto>.ErrorResponse("Failed to save skill"));
            }
        }

        /// <summary>
        /// Toggle skill active/inactive status.
        /// </summary>
        [HttpPatch("{skillId}/toggle")]
        public async Task<ActionResult<ApiResponse<bool>>> ToggleSkillStatus(int skillId)
        {
            try
            {
                await _skillService.ToggleSkillStatusAsync(skillId);
                return Ok(ApiResponse<bool>.SuccessResponse(true, "Skill status updated successfully"));
            }
            catch (NotFoundException ex)
            {
                _logger.LogWarning("Failed to toggle skill {SkillId}: {Message}", skillId, ex.Message);
                return NotFound(ApiResponse<bool>.ErrorResponse(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle skill {SkillId}", skillId);
                return StatusCode(500, ApiResponse<bool>.ErrorResponse("Failed to toggle skill status"));
            }
        }
    }
}