namespace Auth.Models.DTOs
{
    public class JournalSubmissionStatusDto
    {
        public string MonthYear { get; set; } = string.Empty;
        public bool Submitted { get; set; } = false;
    }
}
