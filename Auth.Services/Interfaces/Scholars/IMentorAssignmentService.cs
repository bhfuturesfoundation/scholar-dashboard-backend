using Auth.Models.DTOs.Scholars;

namespace Auth.Services.Interfaces.Scholars
{
    public class MentorPairingOptions
    {
        /// <summary>Validate and report without writing. Default.</summary>
        public bool DryRun { get; set; } = true;

        /// <summary>
        /// Replace an existing mentor when the scholar already has a different one.
        /// Off by default — a reassignment is a decision, not a side effect of re-uploading
        /// a list.
        /// </summary>
        public bool ReassignExisting { get; set; }
    }

    /// <summary>
    /// Assigning scholars to mentors.
    ///
    /// This exists because the startup seeder did it from a spreadsheet and reported its
    /// failures to a log nobody reads — 22 scholars went unmentored on every single boot
    /// because the mentors sheet referenced addresses with no matching account. The same
    /// pairing work now happens where a human can see what didn't match and fix it.
    /// </summary>
    public interface IMentorAssignmentService
    {
        Task<MentorAssignmentOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);

        /// <summary>Scholars and their current mentor, filterable to the unassigned.</summary>
        Task<List<MenteeAssignmentDto>> GetScholarsAsync(
            bool onlyUnassigned = false, string? search = null, CancellationToken cancellationToken = default);

        /// <summary>Everyone in the Mentor role, with how many mentees they carry.</summary>
        Task<List<MentorSummaryDto>> GetMentorsAsync(CancellationToken cancellationToken = default);

        Task AssignAsync(string scholarId, string mentorId, CancellationToken cancellationToken = default);

        Task UnassignAsync(string scholarId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Pairs scholars to mentors from a spreadsheet of mentor-email / scholar-email rows.
        ///
        /// Rows whose scholar or mentor has no account are returned as issues rather than
        /// logged and forgotten. That is the whole point: the missing 22 become a list
        /// someone can act on.
        /// </summary>
        Task<MentorPairingResultDto> ImportPairingsAsync(
            Stream fileStream,
            string fileName,
            MentorPairingOptions options,
            CancellationToken cancellationToken = default);
    }
}
