using Auth.Models.DTOs.Admin;
using Auth.Models.Request;

namespace Auth.Services.Interfaces
{
    /// <summary>
    /// The help-desk side of accounts: everything an administrator does when somebody says
    /// "I can't get in". Each action is audited with the administrator as the actor and the
    /// account as the target.
    /// </summary>
    public interface IAdminAccountService
    {
        /// <summary>
        /// Creates one account with a generated password and, by default, emails a link to
        /// choose their own. A failed email does not undo the account: the admin still gets
        /// the temporary password to hand over another way.
        /// </summary>
        Task<CreatedAccountDto> CreateAccountAsync(
            string adminId, AdminCreateUserRequest request, CancellationToken cancellationToken = default);

        Task<AdminAccountDetailDto> GetAccountAsync(string userId, CancellationToken cancellationToken = default);

        Task<AdminAccountDetailDto> UpdateAccountAsync(
            string adminId, string userId, AdminUpdateUserRequest request, CancellationToken cancellationToken = default);

        /// <summary>Clears a lockout from too many wrong passwords and resets the counter.</summary>
        Task UnlockAsync(string adminId, string userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Emails the person a reset link. Throws <see cref="InvalidOperationException"/> when
        /// the email provider fails, so the admin is told to fall back to a temporary password.
        /// </summary>
        Task SendPasswordResetEmailAsync(string adminId, string userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Replaces the password with a generated one, forces a change at next sign-in, clears
        /// any lockout and ends every existing session. For when email is not reaching them.
        /// </summary>
        Task<TemporaryPasswordDto> SetTemporaryPasswordAsync(
            string adminId, string userId, string? ipAddress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Permanently deletes accounts and everything they wrote: journals, goals, kudos,
        /// notifications, mentor links. Only for accounts that are already deactivated, or
        /// alumni and withdrawn scholars, and never for an admin or the caller. Accounts that
        /// don't qualify are skipped with a reason rather than failing the whole request.
        /// </summary>
        Task<DeleteAccountsResultDto> DeleteAccountsAsync(
            string adminId, IReadOnlyCollection<string> userIds, string? ipAddress = null, CancellationToken cancellationToken = default);

        /// <summary>Ends every session on every device. Returns how many were live.</summary>
        Task<int> SignOutEverywhereAsync(
            string adminId, string userId, string? ipAddress = null, CancellationToken cancellationToken = default);
    }
}
