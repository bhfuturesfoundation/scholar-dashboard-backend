using Auth.Models.Entities.Scholars;
using Auth.Models.Enums.Scholars;
using Microsoft.AspNetCore.Identity;

namespace Auth.Models.Entities
{
    public class User : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Title { get; set; }

        /// <summary>This account's mentors, when it is a scholar.</summary>
        public ICollection<MentorAssignment> MentorAssignments { get; set; } = new List<MentorAssignment>();

        /// <summary>The scholars this account mentors, when it is a mentor.</summary>
        public ICollection<MentorAssignment> MenteeAssignments { get; set; } = new List<MentorAssignment>();

        public DateTime? UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }

        /// <summary>
        /// Bumped whenever every issued access token for this account must stop working —
        /// today only by "sign out everywhere".
        ///
        /// An access token carries the version it was minted under as a <c>tv</c> claim, and
        /// authentication rejects any token whose claim no longer matches. This is what makes
        /// revocation immediate: without it, revoking refresh tokens only stops *renewal*, and
        /// a stolen access token kept working until it expired on its own.
        ///
        /// An integer rather than a timestamp deliberately. A timestamp comparison has to
        /// decide what to do with a token minted in the same second as the revocation — reject
        /// it and an immediate re-login breaks, accept it and the revocation has a one-second
        /// hole. A counter has no such boundary.
        /// </summary>
        public int TokenVersion { get; set; }

        // ── Scholar lifecycle ─────────────────────────────────────────────────

        /// <summary>
        /// Typed programme status. Drives promotion and audience logic; <see cref="Title"/>
        /// remains the free-text label shown in the UI and kept in sync on every transition.
        /// </summary>
        public ScholarStatus ScholarStatus { get; set; } = ScholarStatus.Unassigned;

        /// <summary>
        /// Intake cohort. Kept for life, including after becoming alumni — "which generation
        /// was this alumnus from" is unanswerable if it is inferred from a status that
        /// changes every year. Null for staff accounts and un-backfilled records.
        /// </summary>
        public int? GenerationId { get; set; }
        public ScholarGeneration? Generation { get; set; }
    }
}
