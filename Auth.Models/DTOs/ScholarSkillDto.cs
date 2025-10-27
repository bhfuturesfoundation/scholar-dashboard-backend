namespace Auth.Models.DTOs
{
    public class ScholarSkillDto
    {
        public int SkillId { get; set; }
        public string ScholarId { get; set; }
        public string QuestionText { get; set; }
        public string SkillAnswer { get; set; }
        public bool Active { get; set; }
    }
}
