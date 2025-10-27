namespace Auth.Models.DTOs
{
    public class JournalQuestionDto
    {
        public int QuestionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Type { get; set; } = "Text";
        public bool IsSkill { get; set; }
        public int Order { get; set; }

        // Answer info
        public string? Response { get; set; }
        public string? SkillAnswer { get; set; }
    }

}
