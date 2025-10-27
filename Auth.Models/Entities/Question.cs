namespace Auth.Models.Entities
{
    public class Question
    {
        public int QuestionId { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Type { get; set; } = "Text";
        public bool Active { get; set; } = true;
        public int Order { get; set; }
        public bool IsSkill { get; set; }
    }
}
