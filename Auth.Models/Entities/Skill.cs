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

        /// <summary>
        /// Self-reported satisfaction with this goal at the time it was set (e.g. the intake
        /// form's "2 = partially dissatisfied"). Free text because the rating scale's wording
        /// has changed between intake cycles and snapshotting it, like <see cref="Answer.QuestionTextSnapshot"/>,
        /// keeps old rows meaning what they said when written. Null for rows created before
        /// this existed.
        /// </summary>
        public string? SatisfactionRating { get; set; }
    }
}
