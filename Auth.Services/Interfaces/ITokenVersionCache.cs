namespace Auth.Services.Interfaces
{
    /// <summary>
    /// Checks an access token's generation against the account's current one, cheaply
    /// enough to run on every authenticated request.
    ///
    /// This is what makes "sign out everywhere" mean anything. Revoking refresh tokens only
    /// stops renewal; without this the access token already in a stolen browser kept working
    /// until it expired on its own.
    /// </summary>
    public interface ITokenVersionCache
    {
        Task<int> GetCurrentVersionAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>Whether a token minted at <paramref name="tokenVersion"/> is still accepted.</summary>
        Task<bool> IsTokenCurrentAsync(
            string userId, int tokenVersion, CancellationToken cancellationToken = default);

        /// <summary>Drops the cached version so the next check re-reads it.</summary>
        void Invalidate(string userId);
    }
}
