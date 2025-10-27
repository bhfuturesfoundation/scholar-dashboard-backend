namespace Auth.Models.Entities
{
    public class Skill
    {
        public int SkillId { get; set; }
        public string ScholarId { get; set; } = string.Empty;
        public User Scholar { get; set; }
        public int QuestionId { get; set; }
        public Question Question { get; set; }
        public string SkillAnswer { get; set; } = string.Empty;
        public bool Active { get; set; } = true;
        public int Slot { get; set; }
    }
}
