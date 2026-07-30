namespace Auth.Models.DTOs.Email
{
    /// <summary>
    /// Outcome of a single send attempt.
    /// Providers return this instead of throwing so the dispatcher can fall back to
    /// another provider without paying the cost of exception-driven control flow.
    /// </summary>
    public class EmailSendResult
    {
        public bool Success { get; init; }

        /// <summary>Provider key that actually handled (or failed) the send — e.g. "smtp".</summary>
        public string Provider { get; init; } = string.Empty;

        /// <summary>Provider-side message id, when the transport returns one.</summary>
        public string? MessageId { get; init; }

        public string? Error { get; init; }

        /// <summary>
        /// True when the failure is worth retrying on a different provider
        /// (network blip, rate limit, 5xx). False for permanent failures such as
        /// an invalid recipient address, which would fail identically everywhere.
        /// </summary>
        public bool IsTransient { get; init; }

        /// <summary>
        /// True when the send was deliberately skipped because the recipient is suppressed
        /// (deactivated account, unsubscribed firm, bounce). Distinct from a failure: nothing
        /// went wrong, so campaign stats must report this as skipped rather than failed and
        /// retry must not pick it up.
        /// </summary>
        public bool WasSuppressed { get; init; }

        public static EmailSendResult Ok(string provider, string? messageId = null) =>
            new() { Success = true, Provider = provider, MessageId = messageId };

        public static EmailSendResult Fail(string provider, string error, bool isTransient = true) =>
            new() { Success = false, Provider = provider, Error = error, IsTransient = isTransient };

        public static EmailSendResult Suppressed(string reason, string? explanation) =>
            new()
            {
                Success = false,
                WasSuppressed = true,
                IsTransient = false,
                Provider = "suppressed",
                Error = explanation ?? reason
            };
    }
}
