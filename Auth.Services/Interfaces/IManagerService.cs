using Auth.Models.DTOs;
using Auth.Models.Response;

namespace Auth.Services.Interfaces
{
    public interface IManagerService
    {
        Task<List<JournalAnswerResponse>> GetJournalForUserAsync(string scholarId, string monthYear);
        Task<List<JournalSubmissionStatusDto>> GetUserSubmissionsAsync(string userId);
        Task<List<ScholarJournalOverviewDto>> GetJournalOverviewAsync();
        Task<UserDetailsResponse?> GetUserByIdAsync(string userId);
    }
}
