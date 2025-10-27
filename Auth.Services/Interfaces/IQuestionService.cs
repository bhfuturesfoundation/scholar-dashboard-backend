using Auth.Models.Entities;

namespace Auth.Services.Interfaces
{
    public interface IQuestionService
    {
        Task<IEnumerable<Question>> GetActiveQuestionsAsync();
        Task<IEnumerable<Question>> GetInactiveQuestionsAsync();
        Task ReactivateQuestionAsync(int questionId);
        Task DeactivateQuestionAsync(int questionId);
        Task DeleteQuestionAsync(int questionId);
        Task<Question> CreateQuestionAsync(Question question);
        Task UpdateQuestionAsync(int questionId, Question updated);
    }
}
