using Auth.Models.Entities;

namespace Auth.Models.Results
{
    /// <summary>Why a credential check failed, so the API can say something useful.</summary>
    public enum CredentialFailureReason
    {
        None = 0,

        /// <summary>No such account, or the password was wrong. Reported identically on purpose.</summary>
        InvalidCredentials = 1,

        /// <summary>Too many failed attempts; the account is temporarily locked.</summary>
        LockedOut = 2,

        /// <summary>The account has been deactivated by an administrator.</summary>
        Disabled = 3
    }

    /// <summary>
    /// Outcome of verifying an email/password pair.
    ///
    /// This replaced a four-element positional tuple whose last field was named
    /// <c>EmailNotConfirmed</c> but was destructured by the caller as <c>emailConfirmed</c> —
    /// so the API reported every confirmed user as unconfirmed and vice versa. Named
    /// properties make that class of mistake a compile error rather than a silent inversion.
    /// </summary>
    public class CredentialVerificationResult
    {
        public bool Succeeded { get; init; }

        public User? User { get; init; }

        public bool RequiresTwoFactor { get; init; }

        /// <summary>True when the account's email address has been confirmed.</summary>
        public bool EmailConfirmed { get; init; }

        public CredentialFailureReason FailureReason { get; init; } = CredentialFailureReason.None;

        /// <summary>When the lockout ends, for <see cref="CredentialFailureReason.LockedOut"/>.</summary>
        public DateTimeOffset? LockoutEnd { get; init; }

        public static CredentialVerificationResult Fail(CredentialFailureReason reason, DateTimeOffset? lockoutEnd = null) =>
            new() { Succeeded = false, FailureReason = reason, LockoutEnd = lockoutEnd };

        public static CredentialVerificationResult Ok(User user, bool requiresTwoFactor, bool emailConfirmed) =>
            new()
            {
                Succeeded = true,
                User = user,
                RequiresTwoFactor = requiresTwoFactor,
                EmailConfirmed = emailConfirmed
            };
    }
}
