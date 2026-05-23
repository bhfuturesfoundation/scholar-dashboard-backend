using Auth.Models.DTOs;
using Auth.Models.Response;

namespace Auth.Services.Interfaces
{
    public interface IManagerService
    {
        Task<List<JournalAnswerResponse>> GetJournalForUserAsync(string scholarId, string monthYear);
        Task<List<JournalSubmissionStatusDto>> GetUserSubmissionsAsync(string userId);
        Task<PagedResult<ScholarJournalOverviewDto>> GetJournalOverviewAsync(int page = 1, int pageSize = 100);
        Task<UserDetailsResponse?> GetUserByIdAsync(string userId);
    }
}
