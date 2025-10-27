namespace Auth.Models.Entities
{
    public class JournalSubmission
    {
        public int Id { get; set; }
        public string ScholarId { get; set; }
        public string MonthYear { get; set; }
        public bool Submitted { get; set; } = false;
        public DateTime SubmittedAt { get; set; }
    }
}
