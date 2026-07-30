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

        // Mentor relationship
        public string? MentorId { get; set; }
        public User? Mentor { get; set; }

        // Mentor → many mentees
        public ICollection<User> Scholars { get; set; } = new List<User>();

        public DateTime? UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }
        public bool AllowMentorJournalAccess { get; set; } = false;

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
