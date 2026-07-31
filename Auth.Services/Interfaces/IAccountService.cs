using Auth.Models.DTOs.Account;

namespace Auth.Services.Interfaces
{
    /// <summary>
    /// Self-service account operations — the things a person may do to their own account
    /// without an administrator.
    ///
    /// Before this existed the only self-service action in the whole system was changing a
    /// password. Someone whose surname was misspelled at intake had to email staff, and
    /// someone who had signed in on a shared machine had no way to end that session.
    /// </summary>
    public interface IAccountService
    {
        /// <summary>Everything the settings screen shows, in one round trip.</summary>
        Task<AccountOverviewDto> GetOverviewAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Updates the caller's own name. Returns the refreshed overview so the client does
        /// not have to re-fetch.
        /// </summary>
        Task<AccountOverviewDto> UpdateProfileAsync(
            string userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Revokes every refresh token for this account, signing out all devices including
        /// the one making the request.
        ///
        /// The caller's current access token stays valid until it expires — revoking issued
        /// JWTs would need a denylist checked on every request, which is a real cost for a
        /// rare action. The practical effect is that every other device is locked out within
        /// the access-token lifetime and cannot renew.
        /// </summary>
        Task<int> SignOutEverywhereAsync(
            string userId, string? ipAddress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// A copy of everything this account holds — profile, journal answers, achievements,
        /// kudos given and received.
        ///
        /// Scoped to the caller's own id, never a parameter. Journal entries are personal
        /// reflections written in confidence, so an endpoint that accepted a user id would
        /// be the single worst data leak in the system.
        /// </summary>
        Task<byte[]> ExportOwnDataAsync(string userId, CancellationToken cancellationToken = default);
    }
}
