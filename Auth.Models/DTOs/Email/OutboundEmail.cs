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
        /// Free-form tag echoed into logs and the campaign audit trail — useful for
        /// correlating a delivery back to the campaign that produced it.
        /// </summary>
        public string? Tag { get; set; }
    }
}
