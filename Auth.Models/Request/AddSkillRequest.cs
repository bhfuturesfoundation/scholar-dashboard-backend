namespace Auth.Models.Request
{
    public class AddSkillRequest
    {
        public int QuestionId { get; set; }
        public string SkillAnswer { get; set; } = string.Empty;
        public int Slot { get; set; }
    }
}
