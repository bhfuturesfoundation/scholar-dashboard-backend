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
    [Authorize]
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
        /// Get all active skills for a scholar.
        /// </summary>
        [HttpGet("{scholarId}")]
        public async Task<ActionResult<ApiResponse<IEnumerable<ScholarSkillDto>>>> GetActiveSkills(string scholarId)
        {
            var skills = await _skillService.GetActiveSkillsAsync(scholarId);
            return Ok(ApiResponse<IEnumerable<ScholarSkillDto>>.SuccessResponse(skills, $"Fetched {skills.Count()} active skills for scholar {scholarId}"));
        }

        /// <summary>
        /// Add a new skill for a scholar.
        /// </summary>
        [HttpPost("{scholarId}")]
        public async Task<ActionResult<ApiResponse<Skill>>> AddSkill(string scholarId, [FromBody] AddSkillRequest request)
        {
            if (request == null)
                return BadRequest(ApiResponse<Skill>.ErrorResponse("Request body cannot be null"));

            // Ensure slot is provided in request
            if (request.Slot < 1 || request.Slot > 4)
                return BadRequest(ApiResponse<Skill>.ErrorResponse("Slot must be between 1 and 4"));

            var skill = new Skill
            {
                QuestionId = request.QuestionId,
                SkillAnswer = request.SkillAnswer
            };

            try
            {
                await _skillService.AddSkillAsync(scholarId, skill, request.Slot);
                return Ok(ApiResponse<Skill>.SuccessResponse(skill, "Skill added successfully"));
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning("Failed to add skill for scholar {ScholarId}: {Message}", scholarId, ex.Message);
                return BadRequest(ApiResponse<Skill>.ErrorResponse(ex.Message));
            }
        }


        /// <summary>
        /// Deactivate a skill.
        /// </summary>
        [HttpPatch("deactivate/{skillId}")]
        public async Task<ActionResult<ApiResponse<bool>>> DeactivateSkill(int skillId)
        {
            try
            {
                await _skillService.DeactivateSkillAsync(skillId);
                return Ok(ApiResponse<bool>.SuccessResponse(true, "Skill deactivated successfully"));
            }
            catch (NotFoundException ex)
            {
                _logger.LogWarning("Failed to deactivate skill {SkillId}: {Message}", skillId, ex.Message);
                return NotFound(ApiResponse<bool>.ErrorResponse(ex.Message));
            }
        }
    }
}
