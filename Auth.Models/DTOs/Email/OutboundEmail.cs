namespace Auth.Models.DTOs.Email
{
    /// <summary>
    /// Provider-agnostic representation of a single outbound email.
    /// Every <c>IEmailProvider</c> receives this shape and is responsible for
    /// translating it into whatever payload its transport expects.
    /// </summary>
    public class OutboundEmail
    {
        public string ToEmail { get; set; } = string.Empty;
        public string? ToName { get; set; }

        public string Subject { get; set; } = string.Empty;

        /// <summary>Fully rendered HTML body.</summary>
        public string HtmlBody { get; set; } = string.Empty;

        /// <summary>Plain-text fallback. Generated from the HTML body when not supplied.</summary>
        public string? TextBody { get; set; }

        /// <summary>Overrides the provider's configured from-address when set.</summary>
        public string? FromEmail { get; set; }
        public string? FromName { get; set; }

        public string? ReplyTo { get; set; }

        /// <summary>
        /// Addresses to copy, comma or semicolon separated.
        ///
        /// Carried on the message rather than configured per provider because it is per
        /// *recipient*: the partnerships team keeps a CC on many firms — usually the general
        /// office inbox — and several organisations only ever reply when it is present.
        ///
        /// Not every provider can send a CC. Those that cannot ignore this rather than failing,
        /// because losing the copy is a smaller harm than losing the email.
        /// </summary>
        public string? Cc { get; set; }

        /// <summary>
        /// Free-form tag echoed into logs and the campaign audit trail — useful for
        /// correlating a delivery back to the campaign that produced it.
        /// </summary>
        public string? Tag { get; set; }
    }
}
