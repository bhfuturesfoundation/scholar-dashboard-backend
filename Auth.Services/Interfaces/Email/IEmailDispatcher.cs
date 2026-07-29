using Auth.Models.DTOs.Email;

namespace Auth.Services.Interfaces.Email
{
    /// <summary>
    /// Chooses which <see cref="IEmailProvider"/> handles a send and, when enabled,
    /// retries on the next configured provider if the first one fails transiently.
    /// </summary>
    public interface IEmailDispatcher
    {
        /// <summary>
        /// Sends via <paramref name="preferredProviderKey"/> when supplied and configured,
        /// otherwise via the configured default, otherwise via the first configured provider.
        /// </summary>
        Task<EmailSendResult> SendAsync(
            OutboundEmail email,
            string? preferredProviderKey = null,
            CancellationToken cancellationToken = default);

        /// <summary>Every registered provider, configured or not — for the settings UI.</summary>
        IReadOnlyList<IEmailProvider> GetProviders();

        /// <summary>The provider key used when a caller doesn't specify one.</summary>
        string? DefaultProviderKey { get; }
    }
}
