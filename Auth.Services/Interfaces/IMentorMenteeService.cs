using Auth.Models.DTOs;

namespace Auth.Services.Interfaces
{
    public interface IMentorMenteeService
    {
        Task<MentorDto> GetMentorByIdAsync(string mentorId);
        Task<MentorDto> GetMentorByEmailAsync(string mentorEmail);
        Task<List<MenteeDto>> GetMenteesByMentorIdAsync(string mentorId);
        Task<List<MenteeDto>> GetMenteesByMentorEmailAsync(string mentorEmail);
        Task<MentorWithMenteesDto> GetMentorWithMenteesAsync(string mentorId);
        Task<MentorWithMenteesDto> GetMentorWithMenteesByEmailAsync(string mentorEmail);
        Task<MenteeJournalDto> GetMenteeJournalsAsync(string menteeId);
        Task<MenteeJournalDetailDto> GetMenteeJournalForMonthAsync(string menteeId, string monthYear);
        Task<List<MenteeJournalDto>> GetAllMenteesJournalsForMentorAsync(string mentorId);
        Task<bool> IsMentorAsync(string userId);
        Task<bool> IsMenteeOfMentorAsync(string menteeId, string mentorId);
        Task<MentorDto> GetMentorByMenteeIdAsync(string menteeId);
        Task<MenteeWithMentorDto> GetMenteeWithMentorAsync(string menteeId);
        Task<bool> IsMenteeAsync(string userId);
        Task<MenteeDto> GetMenteeByIdAsync(string menteeId);

        // Journal permission methods
        Task<bool> HasMentorJournalAccessAsync(string menteeId);
        Task<bool> UpdateMentorJournalAccessAsync(string menteeId, bool allowAccess);
        Task<bool> CanMentorAccessJournalAsync(string mentorId, string menteeId);
    }
}
