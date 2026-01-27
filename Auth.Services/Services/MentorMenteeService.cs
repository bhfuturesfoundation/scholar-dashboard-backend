using Auth.Models.Data;
using Auth.Models.DTOs;
using Auth.Models.Entities;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
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

            var isMentor = await _userManager.IsInRoleAsync(mentor, "Mentor");
            if (!isMentor)
            {
                _logger.LogWarning("User {MentorId} is not a mentor", mentorId);
                return null;
            }

            return new MentorDto
            {
                MentorId = mentor.Id,
                MentorEmail = mentor.Email,
                MentorName = $"{mentor.FirstName} {mentor.LastName}",
                FirstName = mentor.FirstName,
                LastName = mentor.LastName
            };
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
            var mentees = await _context.Users
                .Where(u => u.MentorId == mentorId && u.IsActive)
                .Select(u => new MenteeDto
                {
                    MenteeId = u.Id,
                    MenteeEmail = u.Email,
                    MenteeName = $"{u.FirstName} {u.LastName}",
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Title = u.Title,
                    AllowMentorJournalAccess = u.AllowMentorJournalAccess
                })
                .OrderBy(m => m.FirstName)
                .ThenBy(m => m.LastName)
                .ToListAsync();

            _logger.LogInformation("Found {Count} mentees for mentor {MentorId}", mentees.Count, mentorId);
            return mentees;
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
            if (mentor == null)
            {
                return null;
            }

            var mentees = await GetMenteesByMentorIdAsync(mentorId);

            return new MentorWithMenteesDto
            {
                Mentor = mentor,
                Mentees = mentees
            };
        }

        public async Task<MentorWithMenteesDto> GetMentorWithMenteesByEmailAsync(string mentorEmail)
        {
            var mentor = await GetMentorByEmailAsync(mentorEmail);
            if (mentor == null)
            {
                return null;
            }

            var mentees = await GetMenteesByMentorEmailAsync(mentorEmail);

            return new MentorWithMenteesDto
            {
                Mentor = mentor,
                Mentees = mentees
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

            _logger.LogInformation("Found {Count} journal submissions for mentee {MenteeId}", submissions.Count, menteeId);

            return new MenteeJournalDto
            {
                Mentee = new MenteeDto
                {
                    MenteeId = mentee.Id,
                    MenteeEmail = mentee.Email,
                    MenteeName = $"{mentee.FirstName} {mentee.LastName}",
                    FirstName = mentee.FirstName,
                    LastName = mentee.LastName,
                    Title = mentee.Title,
                    AllowMentorJournalAccess = mentee.AllowMentorJournalAccess
                },
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

            _logger.LogInformation("Fetched {Count} questions for mentee {MenteeId} in {MonthYear}, submitted: {Submitted}",
                questionDtos.Count, menteeId, monthYear, submission?.Submitted ?? false);

            return new MenteeJournalDetailDto
            {
                Mentee = new MenteeDto
                {
                    MenteeId = mentee.Id,
                    MenteeEmail = mentee.Email,
                    MenteeName = $"{mentee.FirstName} {mentee.LastName}",
                    FirstName = mentee.FirstName,
                    LastName = mentee.LastName,
                    Title = mentee.Title,
                    AllowMentorJournalAccess = mentee.AllowMentorJournalAccess
                },
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

            foreach (var mentee in mentees)
            {
                // Only include journals if mentee has granted access
                if (mentee.AllowMentorJournalAccess)
                {
                    var menteeJournals = await GetMenteeJournalsAsync(mentee.MenteeId);
                    if (menteeJournals != null)
                    {
                        result.Add(menteeJournals);
                    }
                }
            }

            _logger.LogInformation("Retrieved journals for {Count} mentees (with permission) of mentor {MentorId}",
                result.Count, mentorId);
            return result;
        }

        public async Task<bool> IsMentorAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return false;
            }

            return await _userManager.IsInRoleAsync(user, "Mentor");
        }

        public async Task<bool> IsMenteeOfMentorAsync(string menteeId, string mentorId)
        {
            var mentee = await _userManager.FindByIdAsync(menteeId);
            if (mentee == null)
            {
                return false;
            }

            return mentee.MentorId == mentorId;
        }

        public async Task<MentorDto> GetMentorByMenteeIdAsync(string menteeId)
        {
            var mentee = await _userManager.FindByIdAsync(menteeId);
            if (mentee == null)
            {
                _logger.LogWarning("Mentee not found with ID: {MenteeId}", menteeId);
                return null;
            }

            if (string.IsNullOrEmpty(mentee.MentorId))
            {
                _logger.LogInformation("Mentee {MenteeId} does not have an assigned mentor", menteeId);
                return null;
            }

            var mentor = await _userManager.FindByIdAsync(mentee.MentorId);
            if (mentor == null)
            {
                _logger.LogWarning("Mentor user with ID {MentorId} not found for mentee {MenteeId}",
                    mentee.MentorId, menteeId);
                return null;
            }

            _logger.LogInformation("Found mentor {MentorId} ({MentorName}) for mentee {MenteeId}",
                mentor.Id, $"{mentor.FirstName} {mentor.LastName}", menteeId);

            return new MentorDto
            {
                MentorId = mentor.Id,
                MentorEmail = mentor.Email,
                MentorName = $"{mentor.FirstName} {mentor.LastName}",
                FirstName = mentor.FirstName,
                LastName = mentor.LastName
            };
        }

        public async Task<MenteeWithMentorDto> GetMenteeWithMentorAsync(string menteeId)
        {
            var mentee = await _userManager.FindByIdAsync(menteeId);
            if (mentee == null)
            {
                _logger.LogWarning("Mentee not found with ID: {MenteeId}", menteeId);
                return null;
            }

            var menteeDto = new MenteeDto
            {
                MenteeId = mentee.Id,
                MenteeEmail = mentee.Email,
                MenteeName = $"{mentee.FirstName} {mentee.LastName}",
                FirstName = mentee.FirstName,
                LastName = mentee.LastName,
                Title = mentee.Title,
                AllowMentorJournalAccess = mentee.AllowMentorJournalAccess
            };

            MentorDto mentorDto = null;
            if (!string.IsNullOrEmpty(mentee.MentorId))
            {
                mentorDto = await GetMentorByMenteeIdAsync(mentee.Id);
            }

            return new MenteeWithMentorDto
            {
                Mentee = menteeDto,
                Mentor = mentorDto
            };
        }

        public async Task<bool> IsMenteeAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return false;
            }

            return !string.IsNullOrEmpty(user.MentorId);
        }

        public async Task<MenteeDto> GetMenteeByIdAsync(string menteeId)
        {
            var mentee = await _userManager.FindByIdAsync(menteeId);
            if (mentee == null)
            {
                _logger.LogWarning("Mentee not found with ID: {MenteeId}", menteeId);
                return null;
            }

            return new MenteeDto
            {
                MenteeId = mentee.Id,
                MenteeEmail = mentee.Email,
                MenteeName = $"{mentee.FirstName} {mentee.LastName}",
                FirstName = mentee.FirstName,
                LastName = mentee.LastName,
                Title = mentee.Title,
                AllowMentorJournalAccess = mentee.AllowMentorJournalAccess
            };
        }

        // ==================== PERMISSION METHODS ====================

        public async Task<bool> HasMentorJournalAccessAsync(string menteeId)
        {
            var mentee = await _userManager.FindByIdAsync(menteeId);
            if (mentee == null)
            {
                _logger.LogWarning("Mentee not found with ID: {MenteeId}", menteeId);
                return false;
            }

            return mentee.AllowMentorJournalAccess;
        }

        public async Task<bool> UpdateMentorJournalAccessAsync(string menteeId, bool allowAccess)
        {
            var mentee = await _userManager.FindByIdAsync(menteeId);
            if (mentee == null)
            {
                _logger.LogWarning("Mentee not found with ID: {MenteeId}", menteeId);
                return false;
            }

            mentee.AllowMentorJournalAccess = allowAccess;
            mentee.UpdatedAt = DateTime.UtcNow;

            var result = await _userManager.UpdateAsync(mentee);

            if (result.Succeeded)
            {
                _logger.LogInformation("Mentee {MenteeId} {Action} mentor journal access",
                    menteeId, allowAccess ? "granted" : "revoked");
                return true;
            }

            _logger.LogError("Failed to update journal access for mentee {MenteeId}: {Errors}",
                menteeId, string.Join(", ", result.Errors.Select(e => e.Description)));
            return false;
        }

        public async Task<bool> CanMentorAccessJournalAsync(string mentorId, string menteeId)
        {
            // First verify the mentor-mentee relationship
            var isMenteeOfMentor = await IsMenteeOfMentorAsync(menteeId, mentorId);
            if (!isMenteeOfMentor)
            {
                _logger.LogWarning("Mentee {MenteeId} does not belong to mentor {MentorId}",
                    menteeId, mentorId);
                return false;
            }

            // Then check if access is granted
            var hasAccess = await HasMentorJournalAccessAsync(menteeId);

            if (!hasAccess)
            {
                _logger.LogInformation("Mentor {MentorId} does not have journal access for mentee {MenteeId}",
                    mentorId, menteeId);
            }

            return hasAccess;
        }
    }
}