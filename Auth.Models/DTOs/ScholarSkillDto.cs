namespace Auth.Models.DTOs
{
    public class ScholarSkillDto
    {
        public int SkillId { get; set; }
        public string ScholarId { get; set; } = string.Empty;
        public int QuestionId { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public string SkillType { get; set; } = string.Empty; // "Soft", "Hard", "Interpersonal", "Knowledge"
        public string SkillAnswer { get; set; } = string.Empty;
        public bool Active { get; set; }
        public int Slot { get; set; }
    }
}
