namespace Auth.Models.DTOs
{
    public class ScholarJournalOverviewDto
    {
        public string Id { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public List<JournalSubmissionDto> Submissions { get; set; } = new();
    }
}
