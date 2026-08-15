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

        /// <summary>
        /// Whether another active question already occupies <paramref name="order"/>.
        /// </summary>
        /// <remarks>
        /// The unique filtered index on (Order) WHERE Active is what actually guarantees this;
        /// these checks exist so the caller gets a 409 with a sentence they can act on instead
        /// of a raw constraint violation. Only active questions are considered — the archive is
        /// not rendered in sequence, so a deactivated question sharing an order is harmless.
        /// </remarks>
        private Task<bool> OrderTakenAsync(int order, int? excludingQuestionId = null) =>
            _context.Questions.AnyAsync(q =>
                q.Order == order &&
                q.Active &&
                (excludingQuestionId == null || q.QuestionId != excludingQuestionId));

        public async Task<Question> CreateQuestionAsync(Question question)
        {
            if (await OrderTakenAsync(question.Order))
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

            // Create refused a duplicate order; this path did not, so the way to end up with
            // two active questions at position 3 was to create the second at 4 and then edit
            // it. Excludes itself, so saving a question without moving it is not a conflict.
            if (updated.Active && await OrderTakenAsync(updated.Order, questionId))
            {
                _logger.LogWarning(
                    "Question {Id} cannot take order {Order}; another active question holds it",
                    questionId, updated.Order);
                throw new ConflictException($"A question with order {updated.Order} already exists.");
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

            // Restoring an archived question puts it back into the sequence, and its old order
            // may have been handed to something else in the meantime. Without this the restore
            // silently produced the duplicate the create path was written to prevent.
            if (await OrderTakenAsync(existing.Order, questionId))
            {
                _logger.LogWarning(
                    "Question {Id} cannot be reactivated; order {Order} is taken",
                    questionId, existing.Order);
                throw new ConflictException(
                    $"A question with order {existing.Order} already exists. Change the order before reactivating this one.");
            }

            existing.Active = true;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Question {Id} reactivated successfully", questionId);
        }

    }
}
