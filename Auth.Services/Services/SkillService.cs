using Auth.Models.Data;
using Auth.Models.DTOs;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    public class SkillService : ISkillService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SkillService> _logger;

        // Map slots to question orders
        private static readonly Dictionary<int, int> SlotToQuestionOrder = new()
        {
            { 1, 1 }, // Soft Skill
            { 2, 2 }, // Hard Skill
            { 3, 3 }, // Interpersonal Skill
            { 4, 4 }  // Knowledge Skill
        };

        private static readonly Dictionary<int, string> SlotToSkillType = new()
        {
            { 1, "Soft Skill" },
            { 2, "Hard Skill" },
            { 3, "Interpersonal Skill" },
            { 4, "Knowledge Skill" }
        };

        public SkillService(ApplicationDbContext context, ILogger<SkillService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<ScholarSkillDto>> GetSkillsAsync(string scholarId)
        {
            var skills = await _context.Skills
                .Include(s => s.Question)
                .Where(s => s.ScholarId == scholarId)
                .OrderBy(s => s.Slot)
                .ToListAsync();

            var skillDtos = skills.Select(s => new ScholarSkillDto
            {
                SkillId = s.SkillId,
                ScholarId = s.ScholarId,
                QuestionId = s.QuestionId,
                QuestionText = s.Question.Text,
                SkillType = SlotToSkillType.ContainsKey(s.Slot) ? SlotToSkillType[s.Slot] : "Unknown",
                SkillAnswer = s.SkillAnswer,
                Active = s.Active,
                Slot = s.Slot
            }).ToList();

            _logger.LogInformation("Fetched {Count} skills for scholar {ScholarId}", skillDtos.Count, scholarId);
            return skillDtos;
        }

        public async Task<ScholarSkillDto> AddOrUpdateSkillAsync(string scholarId, int slot, string skillAnswer)
        {
            if (slot < 1 || slot > 4)
                throw new ValidationException("Slot must be between 1 and 4");

            if (string.IsNullOrWhiteSpace(skillAnswer))
                throw new ValidationException("Skill answer cannot be empty");

            // Get the question for this slot
            var questionOrder = SlotToQuestionOrder[slot];
            var question = await _context.Questions
                .FirstOrDefaultAsync(q => q.Order == questionOrder && q.IsSkill && q.Active);

            if (question == null)
                throw new ValidationException($"Skill question for slot {slot} not found");

            // Get all skills for this scholar in this slot
            var existingSkills = await _context.Skills
                .Include(s => s.Question)
                .Where(s => s.ScholarId == scholarId && s.Slot == slot)
                .ToListAsync();

            // Find if there's an active skill
            var activeSkill = existingSkills.FirstOrDefault(s => s.Active);

            if (activeSkill != null)
            {
                // Update the active skill
                activeSkill.SkillAnswer = skillAnswer.Trim();
                _context.Skills.Update(activeSkill);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Updated active skill {SkillId} for scholar {ScholarId} in slot {Slot}",
                    activeSkill.SkillId, scholarId, slot);

                return new ScholarSkillDto
                {
                    SkillId = activeSkill.SkillId,
                    ScholarId = activeSkill.ScholarId,
                    QuestionId = activeSkill.QuestionId,
                    QuestionText = activeSkill.Question.Text,
                    SkillType = SlotToSkillType[slot],
                    SkillAnswer = activeSkill.SkillAnswer,
                    Active = activeSkill.Active,
                    Slot = activeSkill.Slot
                };
            }
            else
            {
                // No active skill exists, create a new one
                var newSkill = new Skill
                {
                    ScholarId = scholarId,
                    QuestionId = question.QuestionId,
                    SkillAnswer = skillAnswer.Trim(),
                    Active = true,
                    Slot = slot
                };

                _context.Skills.Add(newSkill);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Created new skill {SkillId} for scholar {ScholarId} in slot {Slot}",
                    newSkill.SkillId, scholarId, slot);

                return new ScholarSkillDto
                {
                    SkillId = newSkill.SkillId,
                    ScholarId = newSkill.ScholarId,
                    QuestionId = newSkill.QuestionId,
                    QuestionText = question.Text,
                    SkillType = SlotToSkillType[slot],
                    SkillAnswer = newSkill.SkillAnswer,
                    Active = newSkill.Active,
                    Slot = newSkill.Slot
                };
            }
        }

        public async Task ToggleSkillStatusAsync(int skillId)
        {
            var skill = await _context.Skills.FirstOrDefaultAsync(s => s.SkillId == skillId);
            if (skill == null)
            {
                _logger.LogWarning("Skill {SkillId} not found", skillId);
                throw new NotFoundException("Skill", skillId.ToString());
            }

            // If activating this skill, deactivate all other skills in the same slot
            if (!skill.Active)
            {
                var otherActiveSkills = await _context.Skills
                    .Where(s => s.ScholarId == skill.ScholarId && s.Slot == skill.Slot && s.Active && s.SkillId != skillId)
                    .ToListAsync();

                foreach (var otherSkill in otherActiveSkills)
                {
                    otherSkill.Active = false;
                    _context.Skills.Update(otherSkill);
                }
            }

            skill.Active = !skill.Active;
            _context.Skills.Update(skill);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Toggled skill {SkillId} to {Status}", skillId, skill.Active ? "active" : "inactive");
        }
    }
}