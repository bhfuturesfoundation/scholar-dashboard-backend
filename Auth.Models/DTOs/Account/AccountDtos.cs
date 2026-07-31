namespace Auth.Models.DTOs.Account
{
    /// <summary>
    /// The fields a person may change about themselves.
    ///
    /// Deliberately does NOT include email, title, roles, generation or scholar status.
    /// Those are programme facts, not preferences: <c>Title</c> and <c>ScholarStatus</c>
    /// drive cohort logic and promotion, roles are the access model, and letting somebody
    /// edit their own email would let them move a password-reset link to an address they
    /// control. All of them stay with an administrator.
    /// </summary>
    public class UpdateProfileRequest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }

    /// <summary>
    /// Everything the settings screen needs about one account, in one request.
    ///
    /// A superset of <c>CurrentUserResponse</c>: it adds the programme facts a person can
    /// see but not change, and the honest state of features that are not switched on. The
    /// settings screen exists to tell someone the truth about their account, which means
    /// it has to be able to say "two-factor is not available on this deployment" rather
    /// than showing a toggle that does nothing.
    /// </summary>
    public class AccountOverviewDto
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        /// <summary>Free-text programme title, e.g. "Senior Scholar". Set by staff.</summary>
        public string Title { get; set; } = string.Empty;

        public List<string> Roles { get; set; } = new();

        public bool IsActive { get; set; }
        public bool EmailConfirmed { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Scholar lifecycle status as an int, or null for staff accounts.</summary>
        public int? ScholarStatus { get; set; }

        public string? GenerationName { get; set; }

        /// <summary>Mentor's display name, when one is assigned.</summary>
        public string? MentorName { get; set; }

        /// <summary>Whether this scholar currently lets their mentor read their journal.</summary>
        public bool? MentorJournalAccess { get; set; }

        // ── Security ──────────────────────────────────────────────────────────

        public bool TwoFactorEnabled { get; set; }

        /// <summary>
        /// False while two-factor is not wired up. The screen shows an honest
        /// "unavailable" instead of a switch that silently does nothing —
        /// <c>UserService</c> forces <c>TwoFactorEnabled</c> to false on every sign-in.
        /// </summary>
        public bool TwoFactorAvailable { get; set; }

        /// <summary>How many refresh tokens are live — effectively signed-in devices.</summary>
        public int ActiveSessions { get; set; }

        /// <summary>Whether this account signs in through Google or GitHub rather than a password.</summary>
        public List<string> ExternalLogins { get; set; } = new();
    }
}
