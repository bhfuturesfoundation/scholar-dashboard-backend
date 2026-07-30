namespace Auth.Models.Entities.Scholars
{
    /// <summary>
    /// A cohort — the group of scholars who joined together, e.g. "Generation 2026".
    ///
    /// Scholars keep their generation for life, including after becoming alumni. That is the
    /// point: "which generation was this alumnus from" is the question the foundation
    /// actually asks, and it is unanswerable if the cohort is inferred from a status that
    /// changes every year.
    /// </summary>
    public class ScholarGeneration
    {
        public int Id { get; set; }

        /// <summary>Display name, e.g. "Generation 2026".</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Intake year. Unique — one generation per year.</summary>
        public int Year { get; set; }

        public string? Description { get; set; }

        public DateTime? StartsOn { get; set; }
        public DateTime? EndsOn { get; set; }

        /// <summary>
        /// The generation new intake is assigned to by default. Exactly one at a time —
        /// setting a new current generation clears the flag on the previous one.
        /// </summary>
        public bool IsCurrent { get; set; }

        public string? CreatedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<User> Scholars { get; set; } = new List<User>();
    }
}
