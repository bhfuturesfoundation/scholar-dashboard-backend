namespace Auth.Models.Entities.Email
{
    /// <summary>
    /// An address that must never be mailed, independent of any user or firm record.
    ///
    /// Needed because suppression outlives the thing that caused it: a firm can be deleted
    /// and re-imported, a user record can be recreated, but "this person asked us to stop"
    /// has to survive both. Deriving suppression purely from User.IsActive and Firm.Status
    /// would quietly resurrect an unsubscribe on the next spreadsheet import.
    /// </summary>
    public class EmailSuppression
    {
        public int Id { get; set; }

        /// <summary>Lowercased, trimmed address. Unique.</summary>
        public string NormalizedEmail { get; set; } = string.Empty;

        /// <summary>Stored as <see cref="Auth.Models.Enums.Email.SuppressionSource"/>.</summary>
        public int Source { get; set; }

        /// <summary>Free-text reason shown in the admin UI.</summary>
        public string? Reason { get; set; }

        /// <summary>Null for a self-service unsubscribe.</summary>
        public string? CreatedByUserId { get; set; }
        public string? CreatedByName { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Set when staff lift a suppression. The row is kept rather than deleted so the
        /// history of "they unsubscribed, then we re-added them, and who did it" survives.
        /// </summary>
        public DateTime? LiftedAt { get; set; }
        public string? LiftedByUserId { get; set; }

        public bool IsActive => LiftedAt is null;
    }
}
