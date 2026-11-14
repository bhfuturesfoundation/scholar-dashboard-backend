namespace Auth.Models.Entities.Scholars
{
    /// <summary>
    /// One mentor assigned to one scholar. A scholar can have more than one mentor and a
    /// mentor more than one scholar, and the scholar decides per mentor whether that mentor
    /// may read their journal.
    /// </summary>
    public class MentorAssignment
    {
        /// <summary>The programme pairs each scholar with up to two mentors.</summary>
        public const int MaxMentorsPerScholar = 2;

        public int Id { get; set; }

        public string ScholarId { get; set; } = string.Empty;
        public User? Scholar { get; set; }

        public string MentorId { get; set; } = string.Empty;
        public User? Mentor { get; set; }

        public bool AllowJournalAccess { get; set; }
        public DateTime AssignedAt { get; set; }
    }
}
