using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    public class QuestionService : IQuestionService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<QuestionService> _logger;

        public QuestionService(ApplicationDbContext context, ILogger<QuestionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<Question>> GetActiveQuestionsAsync()
        {
            var questions = await _context.Questions
                .Where(q => q.Active)
                .OrderBy(q => q.Order)
                .ToListAsync();

            _logger.LogInformation("Fetched {Count} active questions", questions.Count);
            return questions;
        }

        public async Task<IEnumerable<Question>> GetInactiveQuestionsAsync()
        {
            var questions = await _context.Questions
                .Where(q => q.Active == false)
                .OrderBy(q => q.Order)
                .ToListAsync();

            _logger.LogInformation("Fetched {Count} inactive questions", questions.Count);
            return questions;
        }

        public async Task<Question> CreateQuestionAsync(Question question)
        {
            // order check (avoid duplicates in ordering)
            bool orderExists = await _context.Questions.AnyAsync(q => q.Order == question.Order && q.Active);
            if (orderExists)
            {
                _logger.LogWarning("Question with order {Order} already exists", question.Order);
                throw new ConflictException($"A question with order {question.Order} already exists.");
            }

            _context.Questions.Add(question);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Question {Id} created successfully", question.QuestionId);
            return question;
        }

        public async Task UpdateQuestionAsync(int questionId, Question updated)
        {
            var existing = await _context.Questions.FirstOrDefaultAsync(q => q.QuestionId == questionId);
            if (existing == null)
            {
                _logger.LogWarning("Question {Id} not found for update", questionId);
                throw new NotFoundException("Question", questionId.ToString());
            }

            existing.Text = updated.Text;
            existing.Type = updated.Type;
            existing.IsSkill = updated.IsSkill;
            existing.Active = updated.Active;
            existing.Order = updated.Order;

            _context.Questions.Update(existing);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Question {Id} updated successfully", questionId);
        }
        public async Task DeactivateQuestionAsync(int questionId)
        {
            var existing = await _context.Questions.FirstOrDefaultAsync(q => q.QuestionId == questionId);
            if (existing == null)
            {
                _logger.LogWarning("Question {Id} not found for deactivation", questionId);
                throw new NotFoundException("Question", questionId.ToString());
            }

            existing.Active = false;

            _context.Questions.Update(existing);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Question {Id} deactivated successfully", questionId);
        }
        public async Task DeleteQuestionAsync(int questionId)
        {
            var existing = await _context.Questions.FirstOrDefaultAsync(q => q.QuestionId == questionId);
            if (existing == null)
            {
                _logger.LogWarning("Question {Id} not found for deletion", questionId);
                throw new NotFoundException("Question", questionId.ToString());
            }

            _context.Questions.Remove(existing);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Question {Id} deleted permanently", questionId);
        }
        public async Task ReactivateQuestionAsync(int questionId)
        {
            var existing = await _context.Questions.FirstOrDefaultAsync(q => q.QuestionId == questionId);
            if (existing == null)
            {
                _logger.LogWarning("Question {Id} not found for reactivation", questionId);
                throw new NotFoundException("Question", questionId.ToString());
            }

            existing.Active = true;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Question {Id} reactivated successfully", questionId);
        }

    }
}
