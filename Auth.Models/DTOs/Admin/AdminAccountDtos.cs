namespace Auth.Models.DTOs.Admin
{
    /// <summary>
    /// What an administrator needs to answer "why can't this person sign in?" in one view:
    /// whether the account is switched off, locked, waiting on a password change, or has
    /// simply never signed in.
    /// </summary>
    public class AdminAccountDetailDto
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();

        public bool IsActive { get; set; }
        public bool EmailConfirmed { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Set only while the lockout is still in force.</summary>
        public DateTimeOffset? LockedOutUntil { get; set; }

        public int FailedSignInAttempts { get; set; }
        public bool MustChangePassword { get; set; }

        public int ActiveSessions { get; set; }

        /// <summary>
        /// When the newest session was issued — a sign-in or a silent renewal, so read it as
        /// "last seen" rather than "last typed a password". Null when the account has never
        /// signed in.
        /// </summary>
        public DateTime? LastSignInAt { get; set; }

        public int? ScholarStatus { get; set; }
        public string? GenerationName { get; set; }
        public string? MentorName { get; set; }
    }

    /// <summary>
    /// Returned exactly once. Only the hash is stored, so if the admin closes the dialog
    /// without copying it the only way forward is to generate another.
    /// </summary>
    public class TemporaryPasswordDto
    {
        public string TemporaryPassword { get; set; } = string.Empty;
    }

    public class CreatedAccountDto
    {
        public AdminAccountDetailDto Account { get; set; } = new();

        /// <summary>
        /// Shown once, as a fallback for when the welcome email does not arrive. They are
        /// asked to replace it at first sign-in either way.
        /// </summary>
        public string TemporaryPassword { get; set; } = string.Empty;

        public bool WelcomeEmailSent { get; set; }
    }
}
