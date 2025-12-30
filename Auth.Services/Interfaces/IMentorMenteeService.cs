using Auth.Models.DTOs;

namespace Auth.Services.Interfaces
{
    public interface IMentorMenteeService
    {
        /// <summary>
        /// Gets mentor information by mentor ID
        /// </summary>
        Task<MentorDto> GetMentorByIdAsync(string mentorId);

        /// <summary>
        /// Gets mentor information by email
        /// </summary>
        Task<MentorDto> GetMentorByEmailAsync(string mentorEmail);

        /// <summary>
        /// Gets all mentees assigned to a mentor
        /// </summary>
        Task<List<MenteeDto>> GetMenteesByMentorIdAsync(string mentorId);

        /// <summary>
        /// Gets all mentees assigned to a mentor by mentor email
        /// </summary>
        Task<List<MenteeDto>> GetMenteesByMentorEmailAsync(string mentorEmail);

        /// <summary>
        /// Gets complete mentor information with all their mentees
        /// </summary>
        Task<MentorWithMenteesDto> GetMentorWithMenteesAsync(string mentorId);

        /// <summary>
        /// Gets complete mentor information with all their mentees by email
        /// </summary>
        Task<MentorWithMenteesDto> GetMentorWithMenteesByEmailAsync(string mentorEmail);

        /// <summary>
        /// Gets a specific mentee's journal entries
        /// </summary>
        Task<MenteeJournalDto> GetMenteeJournalsAsync(string menteeId);

        /// <summary>
        /// Gets a specific mentee's journal for a particular month
        /// </summary>
        Task<MenteeJournalDetailDto> GetMenteeJournalForMonthAsync(string menteeId, string monthYear);

        /// <summary>
        /// Gets all journals for all mentees of a specific mentor
        /// </summary>
        Task<List<MenteeJournalDto>> GetAllMenteesJournalsForMentorAsync(string mentorId);

        /// <summary>
        /// Verifies if a user is a mentor
        /// </summary>
        Task<bool> IsMentorAsync(string userId);

        /// <summary>
        /// Verifies if a mentee belongs to a specific mentor
        /// </summary>
        Task<bool> IsMenteeOfMentorAsync(string menteeId, string mentorId);

        /// <summary>
        /// Gets a mentee's mentor information by mentee ID
        /// </summary>
        Task<MentorDto> GetMentorByMenteeIdAsync(string menteeId);

        /// <summary>
        /// Gets complete information about a mentee including their mentor
        /// </summary>
        Task<MenteeWithMentorDto> GetMenteeWithMentorAsync(string menteeId);

        /// <summary>
        /// Verifies if a user is a mentee (has a mentor assigned)
        /// </summary>
        Task<bool> IsMenteeAsync(string userId);

        /// <summary>
        /// Gets a mentee's information by their user ID
        /// </summary>
        Task<MenteeDto> GetMenteeByIdAsync(string menteeId);
    }
}
