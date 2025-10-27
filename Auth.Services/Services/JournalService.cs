using Auth.Models.Data;
using Auth.Models.DTOs;
using Auth.Models.Entities;
using Auth.Models.Request;
using Auth.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    public class JournalService : IJournalService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<JournalService> _logger;

        public JournalService(ApplicationDbContext context, ILogger<JournalService> logger)
        {
            _context = context;
            _logger = logger;
        }

        /** --- Get all answers for a scholar for a month --- */
        public async Task<IEnumerable<Answer>> GetScholarAnswersAsync(string scholarId, string monthYear)
        {
            return await _context.Answers
                .Include(a => a.Question)
                .Where(a => a.ScholarId == scholarId && a.MonthYear == monthYear)
                .OrderBy(a => a.Question.Order)
                .ToListAsync();
        }

        /** --- Submit answers and mark month as submitted --- */
        public async Task<bool> SubmitAnswersAsync(SubmitAnswersRequest request)
        {
            if (!request.Answers.Any()) return false;

            foreach (var ansDto in request.Answers)
            {
                var existing = await _context.Answers
                    .FirstOrDefaultAsync(a => a.ScholarId == request.ScholarId
                                              && a.MonthYear == request.MonthYear
                                              && a.QuestionId == ansDto.QuestionId);

                if (existing != null)
                {
                    existing.Response = ansDto.Response;
                    _context.Answers.Update(existing);
                }
                else
                {
                    var answer = new Answer
                    {
                        ScholarId = request.ScholarId,
                        QuestionId = ansDto.QuestionId,
                        MonthYear = request.MonthYear,
                        Response = ansDto.Response
                    };
                    await _context.Answers.AddAsync(answer);
                }
            }

            var saved = await _context.SaveChangesAsync();

            /** --- Mark month as submitted --- */
            var submission = await _context.JournalSubmissions
                .FirstOrDefaultAsync(js => js.ScholarId == request.ScholarId && js.MonthYear == request.MonthYear);

            if (submission == null)
            {
                submission = new JournalSubmission
                {
                    ScholarId = request.ScholarId,
                    MonthYear = request.MonthYear,
                    Submitted = true,
                    SubmittedAt = DateTime.UtcNow
                };
                await _context.JournalSubmissions.AddAsync(submission);
            }
            else
            {
                submission.Submitted = true;
                submission.SubmittedAt = DateTime.UtcNow;
                _context.JournalSubmissions.Update(submission);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation("Submitted {Count} answers and marked month {Month} as submitted for scholar {Scholar}",
                request.Answers.Count, request.MonthYear, request.ScholarId);

            return saved > 0;
        }

        /** --- Get all questions for a month + current answers + submitted state --- */
        public async Task<JournalMonthDto> GetQuestionsForMonthAsync(string scholarId, string monthYear)
        {
            var skills = await _context.Skills
            .Where(s => s.ScholarId == scholarId && s.Active)
            .ToListAsync();


            var questions = await _context.Questions
                .Where(q => q.Active)
                .OrderBy(q => q.Order)
                .ToListAsync();

            var answers = await _context.Answers
                .Where(a => a.ScholarId == scholarId && a.MonthYear == monthYear)
                .ToListAsync();

            var result = questions.Select(q =>
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

            var submission = await _context.JournalSubmissions
                .FirstOrDefaultAsync(js => js.ScholarId == scholarId && js.MonthYear == monthYear);

            var submitted = submission?.Submitted ?? false;

            _logger.LogInformation("Fetched {Count} questions for {Scholar} in {Month}, submitted: {Submitted}",
                result.Count, scholarId, monthYear, submitted);

            return new JournalMonthDto
            {
                Questions = result,
                Submitted = submitted
            };
        }
    }
}
