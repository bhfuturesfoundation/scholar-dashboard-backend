namespace Auth.Models.Entities.Mailing
{
    /// <summary>
    /// Top level of the firm taxonomy — "Financial", "Healthcare", "Legal", "Technology".
    ///
    /// Two levels exist (group → type) because outreach is targeted at both altitudes: a
    /// campaign might go to every bank specifically, or to the whole financial sector.
    /// Flattening this into one list would force the team to multi-select a dozen types
    /// every time they mean "all of finance".
    /// </summary>
    public class FirmGroup
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>URL/CSV-friendly identifier, unique. Generated from the name.</summary>
        public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>Hex colour used for the group's chip in the UI, e.g. "#0b1b3d".</summary>
        public string? ColorHex { get; set; }

        public int SortOrder { get; set; }

        /// <summary>
        /// Seeded groups are marked system: they can be renamed but not deleted, so a
        /// campaign or template can never end up pointing at a vanished taxonomy.
        /// </summary>
        public bool IsSystem { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<FirmType> FirmTypes { get; set; } = new List<FirmType>();
    }
}
