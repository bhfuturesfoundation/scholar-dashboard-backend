using Auth.Models.DTOs.Email;

namespace Auth.Services.Interfaces.Email
{
    /// <summary>
    /// One concrete way of putting an email on the wire (SMTP, GMass, Mailchimp, …).
    ///
    /// This is the Strategy pattern: the callers (notifications, campaigns, password
    /// resets) depend only on this interface, so adding or swapping a vendor never
    /// touches business logic. <see cref="IEmailDispatcher"/> is what picks between them.
    /// </summary>
    public interface IEmailProvider
    {
        /// <summary>
        /// Stable lowercase key used in config, API requests and logs — e.g. "smtp",
        /// "gmass", "mailchimp", "emailjs", "resend", "log". Must be unique.
        /// </summary>
        string Key { get; }

        /// <summary>Human-readable name for the provider picker in the UI.</summary>
        string DisplayName { get; }

        /// <summary>
        /// False when the provider's required environment variables are missing.
        /// Unconfigured providers stay registered (so the UI can explain what's
        /// missing) but the dispatcher will never route to them.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>Why <see cref="IsConfigured"/> is false — surfaced in the settings screen.</summary>
        string? ConfigurationHint { get; }

        Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken cancellationToken = default);
    }
}
