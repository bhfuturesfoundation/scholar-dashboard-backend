namespace Auth.Models.Response
{
    public class JournalAnswerResponse
    {
        public int QuestionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Order { get; set; }
        public string Response { get; set; } = string.Empty;
        public string MonthYear { get; set; } = string.Empty;
    }
}
