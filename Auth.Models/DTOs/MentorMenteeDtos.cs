namespace Auth.Models.DTOs
{
    public class MentorDto
    {
        public string MentorId { get; set; }
        public string MentorEmail { get; set; }
        public string MentorName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }

    public class MenteeDto
    {
        public string MenteeId { get; set; }
        public string MenteeEmail { get; set; }
        public string MenteeName { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Title { get; set; }
    }

    public class MentorWithMenteesDto
    {
        public MentorDto Mentor { get; set; }
        public List<MenteeDto> Mentees { get; set; }
    }

    public class MenteeWithMentorDto
    {
        public MenteeDto Mentee { get; set; }
        public MentorDto Mentor { get; set; }
    }

    public class MenteeJournalDto
    {
        public MenteeDto Mentee { get; set; }
        public List<JournalMonthSummaryDto> Journals { get; set; }
    }

    public class JournalMonthSummaryDto
    {
        public string MonthYear { get; set; }
        public bool Submitted { get; set; }
        public DateTime? SubmittedAt { get; set; }
    }

    public class MenteeJournalDetailDto
    {
        public MenteeDto Mentee { get; set; }
        public string MonthYear { get; set; }
        public List<JournalQuestionDto> Questions { get; set; }
        public bool Submitted { get; set; }
        public DateTime? SubmittedAt { get; set; }
    }
}
