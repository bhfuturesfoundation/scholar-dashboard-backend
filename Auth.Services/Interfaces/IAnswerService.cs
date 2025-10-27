using Auth.Models.Entities;

namespace Auth.Services.Interfaces
{
    public interface IAnswerService
    {
        Task SubmitAnswersAsync(string scholarId, string monthYear, IEnumerable<Answer> answers);
        Task<IEnumerable<Answer>> GetAnswersForMonthAsync(string scholarId, string monthYear);
    }
}
