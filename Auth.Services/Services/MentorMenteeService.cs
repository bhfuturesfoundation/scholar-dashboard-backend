using Auth.Models.Data;
using Auth.Models.DTOs;
using Auth.Models.Entities;
using Auth.Models.Entities.Scholars;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    /// <summary>
    /// Mentor ↔ scholar relationships as seen by the two people in them. A scholar can have
    /// several mentors and grants journal access to each one separately; see
    /// <see cref="MentorAssignment"/>.
    /// </summary>
    public class MentorMenteeService : IMentorMenteeService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<MentorMenteeService> _logger;

        public MentorMenteeService(
            ApplicationDbContext context,
            UserManager<User> userManager,
            ILogger<MentorMenteeService> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<MentorDto> GetMentorByIdAsync(string mentorId)
        {
            var mentor = await _userManager.FindByIdAsync(mentorId);
            if (mentor == null)
            {
                _logger.LogWarning("Mentor not found with ID: {MentorId}", mentorId);
                return null;
            }

            if (!await _userManager.IsInRoleAsync(mentor, "Mentor"))
            {
                _logger.LogWarning("User {MentorId} is not a mentor", mentorId);
                return null;
            }

            return ToMentorDto(mentor);
        }

        public async Task<MentorDto> GetMentorByEmailAsync(string mentorEmail)
        {
            var mentor = await _userManager.FindByEmailAsync(mentorEmail);
            if (mentor == null)
            {
                _logger.LogWarning("Mentor not found with email: {MentorEmail}", mentorEmail);
                return null;
            }

            return await GetMentorByIdAsync(mentor.Id);
        }

        public async Task<List<MenteeDto>> GetMenteesByMentorIdAsync(string mentorId)
        {
            return await _context.MentorAssignments
                .AsNoTracking()
                .Where(a => a.MentorId == mentorId && a.Scholar!.IsActive)
                .Select(a => new MenteeDto
                {
                    MenteeId = a.ScholarId,
                    MenteeEmail = a.Scholar!.Email,
                    MenteeName = a.Scholar.FirstName + " " + a.Scholar.LastName,
                    FirstName = a.Scholar.FirstName,
                    LastName = a.Scholar.LastName,
                    Title = a.Scholar.Title,
                    // Whether the scholar shares with *this* mentor, not with any mentor.
                    AllowMentorJournalAccess = a.AllowJournalAccess
                })
                .OrderBy(m => m.FirstName)
                .ThenBy(m => m.LastName)
                .ToListAsync();
        }

        public async Task<List<MenteeDto>> GetMenteesByMentorEmailAsync(string mentorEmail)
        {
            var mentor = await _userManager.FindByEmailAsync(mentorEmail);
            if (mentor == null)
            {
                _logger.LogWarning("Mentor not found with email: {MentorEmail}", mentorEmail);
                return new List<MenteeDto>();
            }

            return await GetMenteesByMentorIdAsync(mentor.Id);
        }

        public async Task<MentorWithMenteesDto> GetMentorWithMenteesAsync(string mentorId)
        {
            var mentor = await GetMentorByIdAsync(mentorId);
            if (mentor == null) return null;

            return new MentorWithMenteesDto
            {
                Mentor = mentor,
                Mentees = await GetMenteesByMentorIdAsync(mentorId)
            };
        }

        public async Task<MentorWithMenteesDto> GetMentorWithMenteesByEmailAsync(string mentorEmail)
        {
            var mentor = await GetMentorByEmailAsync(mentorEmail);
            if (mentor == null) return null;

            return new MentorWithMenteesDto
            {
                Mentor = mentor,
                Mentees = await GetMenteesByMentorIdAsync(mentor.MentorId)
            };
        }

        public async Task<MenteeJournalDto> GetMenteeJournalsAsync(string menteeId)
        {
            var mentee = await _userManager.FindByIdAsync(menteeId);
            if (mentee == null)
            {
                _logger.LogWarning("Mentee not found with ID: {MenteeId}", menteeId);
                return null;
            }

            var submissions = await _context.JournalSubmissions
                .Where(js => js.ScholarId == menteeId)
                .OrderByDescending(js => js.MonthYear)
                .Select(js => new JournalMonthSummaryDto
                {
                    MonthYear = js.MonthYear,
                    Submitted = js.Submitted,
                    SubmittedAt = js.SubmittedAt
                })
                .ToListAsync();

            return new MenteeJournalDto
            {
                Mentee = await ToMenteeDtoAsync(mentee),
                Journals = submissions
            };
        }

        public async Task<MenteeJournalDetailDto> GetMenteeJournalForMonthAsync(string menteeId, string monthYear)
        {
            var mentee = await _userManager.FindByIdAsync(menteeId);
            if (mentee == null)
            {
                _logger.LogWarning("Mentee not found with ID: {MenteeId}", menteeId);
                return null;
            }

            var skills = await _context.Skills
                .Where(s => s.ScholarId == menteeId && s.Active)
                .ToListAsync();

            var questions = await _context.Questions
                .Where(q => q.Active)
                .OrderBy(q => q.Order)
                .ToListAsync();

            var answers = await _context.Answers
                .Where(a => a.ScholarId == menteeId && a.MonthYear == monthYear)
                .ToListAsync();

            var submission = await _context.JournalSubmissions
                .FirstOrDefaultAsync(js => js.ScholarId == menteeId && js.MonthYear == monthYear);

            var questionDtos = questions.Select(q =>
            {
                var answer = answers.FirstOrDefault(a => a.QuestionId == q.QuestionId);
                var skill = q.IsSkill ? skills.FirstOrDefault(s => s.QuestionId == q.QuestionId)?.SkillAnswer : null;

                return new JournalQuestionDto
                {
                    QuestionId = q.QuestionId,
                    Text = q.Text,
                    Type = q.Type,
                    IsSkill = q.IsSkill,
                    Order = q.Order,
                    Response = answer?.Response,
                    SkillAnswer = skill
                };
            }).ToList();

            return new MenteeJournalDetailDto
            {
                Mentee = await ToMenteeDtoAsync(mentee),
                MonthYear = monthYear,
                Questions = questionDtos,
                Submitted = submission?.Submitted ?? false,
                SubmittedAt = submission?.SubmittedAt
            };
        }

        public async Task<List<MenteeJournalDto>> GetAllMenteesJournalsForMentorAsync(string mentorId)
        {
            var mentees = await GetMenteesByMentorIdAsync(mentorId);
            var result = new List<MenteeJournalDto>();

            foreach (var mentee in mentees.Where(m => m.AllowMentorJournalAccess))
            {
                var menteeJournals = await GetMenteeJournalsAsync(mentee.MenteeId);
                if (menteeJournals == null) continue;

                menteeJournals.Mentee.AllowMentorJournalAccess = true;
                result.Add(menteeJournals);
            }

            return result;
        }

        public async Task<bool> IsMentorAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            return user != null && await _userManager.IsInRoleAsync(user, "Mentor");
        }

        public Task<bool> IsMenteeOfMentorAsync(string menteeId, string mentorId) =>
            _context.MentorAssignments.AnyAsync(a => a.ScholarId == menteeId && a.MentorId == mentorId);

        /// <summary>The first mentor assigned. Kept for screens that show a single mentor.</summary>
        public async Task<MentorDto> GetMentorByMenteeIdAsync(string menteeId) =>
            (await GetMentorsByMenteeIdAsync(menteeId)).FirstOrDefault()?.Mentor;

        public async Task<List<MentorAccessDto>> GetMentorsByMenteeIdAsync(string menteeId)
        {
            var assignments = await _context.MentorAssignments
                .AsNoTracking()
                .Include(a => a.Mentor)
                .Where(a => a.ScholarId == menteeId)
                .OrderBy(a => a.AssignedAt)
                .ThenBy(a => a.Id)
                .ToListAsync();

            return assignments.Select(a => new MentorAccessDto
            {
                Mentor = ToMentorDto(a.Mentor!),
                AllowJournalAccess = a.AllowJournalAccess
            }).ToList();
        }

        public async Task<MenteeWithMentorDto> GetMenteeWithMentorAsync(string menteeId)
        {
            var mentee = await _userManager.FindByIdAsync(menteeId);
            if (mentee == null)
            {
                _logger.LogWarning("Mentee not found with ID: {MenteeId}", menteeId);
                return null;
            }

            return new MenteeWithMentorDto
            {
                Mentee = await ToMenteeDtoAsync(mentee),
                Mentor = await GetMentorByMenteeIdAsync(menteeId)
            };
        }

        public Task<bool> IsMenteeAsync(string userId) =>
            _context.MentorAssignments.AnyAsync(a => a.ScholarId == userId);

        public async Task<MenteeDto> GetMenteeByIdAsync(string menteeId)
        {
            var mentee = await _userManager.FindByIdAsync(menteeId);
            if (mentee == null)
            {
                _logger.LogWarning("Mentee not found with ID: {MenteeId}", menteeId);
                return null;
            }

            return await ToMenteeDtoAsync(mentee);
        }

        // ==================== PERMISSION METHODS ====================

        /// <summary>Whether the scholar shares their journal with at least one mentor.</summary>
        public Task<bool> HasMentorJournalAccessAsync(string menteeId) =>
            _context.MentorAssignments.AnyAsync(a => a.ScholarId == menteeId && a.AllowJournalAccess);

        /// <summary>
        /// Grants or revokes access for one mentor, or for every mentor when
        /// <paramref name="mentorId"/> is null. False when that mentor is not assigned.
        /// </summary>
        public async Task<bool> UpdateMentorJournalAccessAsync(string menteeId, bool allowAccess, string? mentorId = null)
        {
            var assignments = await _context.MentorAssignments
                .Where(a => a.ScholarId == menteeId && (mentorId == null || a.MentorId == mentorId))
                .ToListAsync();

            if (assignments.Count == 0) return false;

            foreach (var assignment in assignments) assignment.AllowJournalAccess = allowAccess;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Mentee {MenteeId} {Action} journal access for {Count} mentor(s)",
                menteeId, allowAccess ? "granted" : "revoked", assignments.Count);
            return true;
        }

        public Task<bool> CanMentorAccessJournalAsync(string mentorId, string menteeId) =>
            _context.MentorAssignments.AnyAsync(a =>
                a.ScholarId == menteeId && a.MentorId == mentorId && a.AllowJournalAccess);

        // ==================== HELPERS ====================

        private static MentorDto ToMentorDto(User mentor) => new()
        {
            MentorId = mentor.Id,
            MentorEmail = mentor.Email,
            MentorName = $"{mentor.FirstName} {mentor.LastName}".Trim(),
            FirstName = mentor.FirstName,
            LastName = mentor.LastName
        };

        private async Task<MenteeDto> ToMenteeDtoAsync(User mentee) => new()
        {
            MenteeId = mentee.Id,
            MenteeEmail = mentee.Email,
            MenteeName = $"{mentee.FirstName} {mentee.LastName}".Trim(),
            FirstName = mentee.FirstName,
            LastName = mentee.LastName,
            Title = mentee.Title,
            AllowMentorJournalAccess = await HasMentorJournalAccessAsync(mentee.Id)
        };
    }
}
