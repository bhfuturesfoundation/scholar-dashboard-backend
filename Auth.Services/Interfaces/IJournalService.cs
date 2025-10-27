using Auth.Models.DTOs;
using Auth.Models.Entities;
using Auth.Models.Request;

namespace Auth.Services.Interfaces
{
    public interface IJournalService
    {
        Task<IEnumerable<Answer>> GetScholarAnswersAsync(string scholarId, string monthYear);
        Task<bool> SubmitAnswersAsync(SubmitAnswersRequest request);
        Task<JournalMonthDto> GetQuestionsForMonthAsync(string scholarId, string monthYear);
    }
}
