namespace Auth.Models.DTOs
{
    public class JournalMonthDto
    {
        public IEnumerable<JournalQuestionDto> Questions { get; set; }
        public bool Submitted { get; set; }
    }
}
