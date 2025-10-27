using Auth.Models.Data;
using Auth.Models.DTOs;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;

namespace Auth.Services.Services
{
    public class SkillService : ISkillService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SkillService> _logger;

        public SkillService(ApplicationDbContext context, ILogger<SkillService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<ScholarSkillDto>> GetActiveSkillsAsync(string scholarId)
        {
            var skills = await _context.Skills
            .Where(s => s.ScholarId == scholarId && s.Active)
            .Select(s => new ScholarSkillDto
            {
                SkillId = s.SkillId,
                ScholarId = s.ScholarId,
                QuestionText = s.Question.Text,
                SkillAnswer = s.SkillAnswer,
                Active = s.Active
            })
            .ToListAsync();

            _logger.LogInformation("Fetched {Count} active skills for scholar {ScholarId}", skills.Count, scholarId);
            return skills;
        }

        public async Task AddSkillAsync(string scholarId, Skill skill, int slot)
        {
            var activeCount = await _context.Skills
                .CountAsync(s => s.ScholarId == scholarId && s.Active);

            if (activeCount >= 4)
                throw new ValidationException("A scholar cannot have more than 4 active skills.");

            var questionExists = await _context.Questions
                .AnyAsync(q => q.QuestionId == skill.QuestionId && q.Active && q.IsSkill);

            if (!questionExists)
                throw new ValidationException($"Question {skill.QuestionId} is not valid or not marked as a skill.");

            var slotTaken = await _context.Skills
                .AnyAsync(s => s.ScholarId == scholarId && s.Slot == slot && s.Active);

            if (slotTaken)
                throw new ValidationException($"Slot {slot} is already assigned for this scholar.");

            skill.ScholarId = scholarId;
            skill.Active = true;
            skill.Slot = slot;

            _context.Skills.Add(skill);
            await _context.SaveChangesAsync();
        }
        public async Task DeactivateSkillAsync(int skillId)
        {
            var skill = await _context.Skills.FirstOrDefaultAsync(s => s.SkillId == skillId);
            if (skill == null)
            {
                _logger.LogWarning("Skill {SkillId} not found for deactivation", skillId);
                throw new NotFoundException("Skill", skillId.ToString());
            }

            if (!skill.Active)
            {
                _logger.LogInformation("Skill {SkillId} already inactive", skillId);
                return;
            }

            skill.Active = false;
            _context.Skills.Update(skill);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Skill {SkillId} deactivated successfully", skillId);
        }
    }
}
