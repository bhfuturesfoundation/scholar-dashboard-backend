namespace Auth.Models.DTOs
{
    public class SubmitAnswerDto
    {
        public int QuestionId { get; set; }
        public string Response { get; set; } = string.Empty;
    }
}
