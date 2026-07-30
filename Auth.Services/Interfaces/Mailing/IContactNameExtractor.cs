using Auth.Models.Enums.Mailing;

namespace Auth.Services.Interfaces.Mailing
{
    /// <summary>A name derived from a firm's email address or name, with how much to trust it.</summary>
    public class ExtractedContactName
    {
        /// <summary>Display name, e.g. "Amir Hodzic". Null when nothing usable was found.</summary>
        public string? FullName { get; init; }

        public string? FirstName { get; init; }
        public string? LastName { get; init; }

        public ContactNameSource Source { get; init; } = ContactNameSource.None;
        public NameConfidence Confidence { get; init; } = NameConfidence.None;

        /// <summary>
        /// Why the result is what it is — shown in the bulk-detect review table so the team
        /// can see "generic mailbox (info@)" rather than an unexplained blank.
        /// </summary>
        public string? Reason { get; init; }

        public bool HasName => !string.IsNullOrWhiteSpace(FullName);

        /// <summary>Whether this is good enough to address a person by.</summary>
        public bool IsUsable => HasName && Confidence >= NameConfidence.Medium;

        public static ExtractedContactName None(string reason) => new() { Reason = reason };
    }

    /// <summary>
    /// Derives a human contact name from a firm's email address, falling back to its name.
    ///
    /// Kept as a pure, injected service with no database or I/O so the rules can be
    /// exhaustively unit-tested — this logic decides how a few hundred real firms get
    /// addressed, and "Dear Info" going out at scale is exactly the failure to prevent.
    /// </summary>
    public interface IContactNameExtractor
    {
        /// <summary>
        /// Best-effort name for a firm. Tries the email local part first (most reliable),
        /// then the firm name. Returns a None result rather than throwing.
        /// </summary>
        ExtractedContactName Extract(string? email, string? firmName = null);

        /// <summary>
        /// Whether an email's local part is a shared/functional mailbox (info@, kontakt@,
        /// noreply@) rather than a person's.
        /// </summary>
        bool IsGenericMailbox(string? email);
    }
}
