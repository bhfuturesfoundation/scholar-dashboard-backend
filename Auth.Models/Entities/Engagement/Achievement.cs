namespace Auth.Models.Entities.Engagement
{
    /// <summary>
    /// A badge a scholar has earned.
    ///
    /// Definitions live in code (<c>AchievementCatalog</c>) rather than the database: they
    /// are logic, not data — each one has an earning rule — and keeping them in code means
    /// adding a badge is a deploy rather than a schema migration plus a rule engine.
    /// This table records only who earned what and when.
    /// </summary>
    public class Achievement
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        /// <summary>Stable key from the catalogue, e.g. "journal-streak-3".</summary>
        public string Key { get; set; } = string.Empty;

        public DateTime EarnedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the scholar has seen it yet, so a newly earned badge can be celebrated
        /// once rather than every page load.
        /// </summary>
        public bool IsSeen { get; set; }
    }
}
