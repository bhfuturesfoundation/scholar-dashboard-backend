namespace Auth.Services.Interfaces.Email
{
    /// <summary>Why an address must not be mailed.</summary>
    public enum SuppressionReason
    {
        None = 0,

        /// <summary>The account exists but has been deactivated. Deactivated means silent.</summary>
        UserInactive = 1,

        /// <summary>The firm asked to stop receiving mail.</summary>
        FirmUnsubscribed = 2,

        /// <summary>Mail to this address hard-bounced.</summary>
        FirmBounced = 3,

        /// <summary>Manually suppressed by staff.</summary>
        FirmDoNotContact = 4,

        /// <summary>On the explicit suppression list, independent of any user or firm record.</summary>
        ManuallySuppressed = 5,

        /// <summary>Not a usable address at all.</summary>
        InvalidAddress = 6
    }

    public class SuppressionCheck
    {
        public bool IsSuppressed { get; init; }
        public SuppressionReason Reason { get; init; } = SuppressionReason.None;

        /// <summary>Human-readable explanation for the delivery log.</summary>
        public string? Explanation { get; init; }

        public static readonly SuppressionCheck Allowed = new();

        public static SuppressionCheck Block(SuppressionReason reason, string explanation) =>
            new() { IsSuppressed = true, Reason = reason, Explanation = explanation };
    }

    /// <summary>
    /// The final gate before any email leaves the system.
    ///
    /// This exists as a service checked inside <c>EmailDispatcher</c> rather than as a filter
    /// on each audience query, because audience queries are written by hand and there are
    /// many of them — FLS campaigns, speaker reminders, mailing campaigns, schedules, plus
    /// whatever gets added next. One of them forgetting <c>IsActive</c> is a matter of time,
    /// and the consequence is mail to someone the foundation has deactivated.
    ///
    /// Audience queries still filter, so we don't waste work building recipients that get
    /// dropped. This is the backstop that makes forgetting harmless rather than harmful.
    /// </summary>
    public interface IEmailSuppressionService
    {
        /// <summary>Whether this address may be mailed, and why not if not.</summary>
        Task<SuppressionCheck> CheckAsync(string? email, CancellationToken cancellationToken = default);

        /// <summary>
        /// Batch form for campaign preflight, so a 400-recipient send performs one query
        /// rather than 400. Returns only the suppressed addresses, keyed by normalised email.
        /// </summary>
        Task<IReadOnlyDictionary<string, SuppressionCheck>> CheckManyAsync(
            IEnumerable<string> emails, CancellationToken cancellationToken = default);
    }
}
