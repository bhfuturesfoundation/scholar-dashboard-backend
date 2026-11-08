using Auth.Models.Entities;

namespace Auth.Services.Interfaces
{
    /// <summary>
    /// Builds and emails a password-reset link.
    ///
    /// Shared by self-service "forgot password" and the admin "send reset email" action so
    /// both produce the same link against the configured frontend, rather than each
    /// hardcoding a host that breaks the day the site moves.
    /// </summary>
    public interface IPasswordResetService
    {
        /// <summary>
        /// Sends the link. Throws <see cref="InvalidOperationException"/> when the email
        /// provider rejects or fails the send, so callers never report success for an email
        /// that was not delivered to the provider.
        /// </summary>
        Task SendResetLinkAsync(User user);

        /// <summary>Same link, worded as a welcome, for an account staff just created.</summary>
        Task SendWelcomeLinkAsync(User user);
    }
}
