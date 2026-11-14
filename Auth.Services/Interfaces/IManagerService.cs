using Auth.Models.DTOs;
using Auth.Models.Response;

namespace Auth.Services.Interfaces
{
    public interface IManagerService
    {
        Task<List<JournalAnswerResponse>> GetJournalForUserAsync(string scholarId, string monthYear);
        Task<List<JournalSubmissionStatusDto>> GetUserSubmissionsAsync(string userId);
        /// <param name="alumni">False: current scholars (juniors, seniors and not-yet-classified). True: alumni.</param>
        Task<PagedResult<ScholarJournalOverviewDto>> GetJournalOverviewAsync(int page = 1, int pageSize = 100, bool alumni = false);
        Task<UserDetailsResponse?> GetUserByIdAsync(string userId);
    }
}
