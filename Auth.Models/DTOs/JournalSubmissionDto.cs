namespace Auth.Models.DTOs
{
    public class JournalSubmissionDto
    {
        public string MonthYear { get; set; } = string.Empty;
        public int? SatisfactionScore { get; set; }
    }
}
