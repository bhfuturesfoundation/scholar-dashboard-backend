using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;

namespace Auth.Services.Services
{
    public class AnswerService : IAnswerService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AnswerService> _logger;

        public AnswerService(ApplicationDbContext context, ILogger<AnswerService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<Answer>> GetAnswersForMonthAsync(string scholarId, string monthYear)
        {
            var answers = await _context.Answers
                .Include(a => a.Question)
                .Where(a => a.ScholarId == scholarId && a.MonthYear == monthYear)
                .ToListAsync();

            if (!answers.Any())
            {
                _logger.LogInformation("No answers found for scholar {ScholarId} in {MonthYear}", scholarId, monthYear);
            }

            return answers;
        }

        public async Task SubmitAnswersAsync(string scholarId, string monthYear, IEnumerable<Answer> answers)
        {
            // ✅ Rule 1: Submissions window
            var today = DateTime.UtcNow;
            if (today.Day < 1 || today.Day > 7)
            {
                _logger.LogWarning("Submission attempt outside allowed window for scholar {ScholarId}", scholarId);
                throw new ValidationException("Submissions are only allowed between the 1st and 7th of the month.");
            }

            // ✅ Rule 2: Prevent duplicate submissions for the same month
            bool alreadySubmitted = await _context.Answers
                .AnyAsync(a => a.ScholarId == scholarId && a.MonthYear == monthYear);

            if (alreadySubmitted)
            {
                _logger.LogWarning("Scholar {ScholarId} already submitted answers for {MonthYear}", scholarId, monthYear);
                throw new ConflictException($"Answers for {monthYear} already exist.");
            }

            // ✅ Validate that all questions exist
            var questionIds = answers.Select(a => a.QuestionId).Distinct().ToList();
            var existingQuestions = await _context.Questions
                .Where(q => questionIds.Contains(q.QuestionId) && q.Active)
                .Select(q => q.QuestionId)
                .ToListAsync();

            if (existingQuestions.Count != questionIds.Count)
            {
                _logger.LogWarning("Submission contains invalid or inactive questions for scholar {ScholarId}", scholarId);
                throw new ValidationException("Submission contains invalid or inactive questions.");
            }

            // ✅ Persist answers
            foreach (var answer in answers)
            {
                answer.ScholarId = scholarId;
                answer.MonthYear = monthYear;
            }

            await _context.Answers.AddRangeAsync(answers);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Scholar {ScholarId} submitted {Count} answers for {MonthYear}",
                scholarId, answers.Count(), monthYear);
        }
    }
}
