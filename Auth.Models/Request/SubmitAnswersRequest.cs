using Auth.Models.DTOs;
namespace Auth.Models.Request
{
    public class SubmitAnswersRequest
    {
        public string ScholarId { get; set; } = string.Empty;
        public string MonthYear { get; set; } = string.Empty; // "YYYY-MM"
        public List<SubmitAnswerDto> Answers { get; set; } = new();
    }
}
