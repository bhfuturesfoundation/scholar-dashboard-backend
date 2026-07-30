namespace Auth.Models.Entities.Mailing
{
    /// <summary>
    /// A kind of organisation — "Bank", "Hospital", "Law Firm", "IT Company".
    /// Created and edited by the partnerships team; the seeded set is only a starting point.
    /// </summary>
    public class FirmType
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>URL/CSV-friendly identifier, unique. Used as the import column value.</summary>
        public string Slug { get; set; } = string.Empty;

        public int? FirmGroupId { get; set; }
        public FirmGroup? FirmGroup { get; set; }

        public string? Description { get; set; }

        /// <summary>
        /// Comma-separated keywords that classify a firm into this type from its name,
        /// website or email domain — e.g. for Bank: "bank,banka,banca,credit union,štedionica".
        ///
        /// Deliberately a plain editable field rather than a hardcoded rule table: the team
        /// can teach the categoriser a new keyword the moment they hit a firm it missed,
        /// without a deploy. <c>IFirmCategorizer</c> is the only consumer.
        /// </summary>
        public string? MatchKeywords { get; set; }

        public string? ColorHex { get; set; }

        public int SortOrder { get; set; }

        /// <summary>Seeded types can be renamed and re-keyworded, but not deleted.</summary>
        public bool IsSystem { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Firm> Firms { get; set; } = new List<Firm>();
        public ICollection<MailingTemplate> Templates { get; set; } = new List<MailingTemplate>();

        /// <summary>Keywords split and trimmed. Empty when none are configured.</summary>
        public IEnumerable<string> Keywords =>
            string.IsNullOrWhiteSpace(MatchKeywords)
                ? Enumerable.Empty<string>()
                : MatchKeywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
