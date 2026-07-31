namespace Auth.Models.Entities.Engagement
{
    /// <summary>
    /// One scholar recognising another.
    ///
    /// Deliberately positive-only: there is no rating, score or downvote. A recognition
    /// feature that can be used negatively becomes a popularity contest with a floor, and in
    /// a cohort of a few dozen people who all know each other that does real damage.
    /// </summary>
    public class Kudos
    {
        public int Id { get; set; }

        public string FromUserId { get; set; } = string.Empty;
        public User FromUser { get; set; } = null!;

        public string ToUserId { get; set; } = string.Empty;
        public User ToUser { get; set; } = null!;

        /// <summary>Stable key from <c>KudosCategories</c>, e.g. "helpful".</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Optional short note. Capped in the service, not just the UI.</summary>
        public string? Message { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Hidden by staff without deleting, so the sender isn't silently confused about
        /// where their message went and the moderation decision stays auditable.
        /// </summary>
        public bool IsHidden { get; set; }
    }
}
