namespace Auth.Models.Entities
{
    public class Answer
    {
        public int AnswerId { get; set; }

        public string ScholarId { get; set; } = string.Empty;
        public User Scholar { get; set; }
        public int QuestionId { get; set; }
        public Question Question { get; set; }
        public string MonthYear { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;

    }
}
