using Auth.Models.Enums.Mailing;

namespace Auth.Models.Entities.Mailing
{
    /// <summary>
    /// A reusable message, optionally scoped to one firm type.
    ///
    /// Both wordings live on ONE row rather than as two linked rows. The pairing is
    /// structural instead of relational, which means there is no way to end up with a
    /// person-variant whose firm-variant was deleted, and the send path never has to
    /// join to find the sibling. <see cref="ResolveSubject"/> picks between them.
    /// </summary>
    public class MailingTemplate
    {
        public int Id { get; set; }

        /// <summary>Internal label, e.g. "Law firm — sponsorship intro".</summary>
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>
        /// Null means generic — usable for any firm. Set means the template is offered
        /// first for firms of that type.
        /// </summary>
        public int? FirmTypeId { get; set; }
        public FirmType? FirmType { get; set; }

        // ── Firm-addressed variant (always required) ──────────────────────────────
        // Used when no trustworthy contact name is known: "Dear Acme d.o.o., ...".

        public string SubjectFirmVariant { get; set; } = string.Empty;
        public string BodyFirmVariant { get; set; } = string.Empty;

        // ── Person-addressed variant (optional) ───────────────────────────────────
        // Used when the firm has a contact name of Medium confidence or better.

        /// <summary>
        /// When false the firm variant is used for everyone, even firms with a known contact.
        /// Lets the team roll out person-addressing template by template.
        /// </summary>
        public bool PersonVariantEnabled { get; set; }

        public string? SubjectPersonVariant { get; set; }
        public string? BodyPersonVariant { get; set; }

        public bool IsActive { get; set; } = true;

        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>True when this template can address a named human.</summary>
        public bool SupportsPersonVariant =>
            PersonVariantEnabled &&
            !string.IsNullOrWhiteSpace(SubjectPersonVariant) &&
            !string.IsNullOrWhiteSpace(BodyPersonVariant);

        /// <summary>
        /// Which variant applies to a firm: person when the template supports it and the
        /// firm has a trustworthy name, otherwise firm.
        /// </summary>
        public TemplateVariant ResolveVariant(bool firmHasUsableContactName) =>
            SupportsPersonVariant && firmHasUsableContactName
                ? TemplateVariant.Person
                : TemplateVariant.Firm;

        public string ResolveSubject(TemplateVariant variant) =>
            variant == TemplateVariant.Person && SupportsPersonVariant
                ? SubjectPersonVariant!
                : SubjectFirmVariant;

        public string ResolveBody(TemplateVariant variant) =>
            variant == TemplateVariant.Person && SupportsPersonVariant
                ? BodyPersonVariant!
                : BodyFirmVariant;
    }
}
