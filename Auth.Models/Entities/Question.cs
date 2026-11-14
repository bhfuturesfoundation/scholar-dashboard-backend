using Auth.Models.Enums;

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

        /// <summary>
        /// Which goal area a skill question asks about. Required when <see cref="IsSkill"/>,
        /// null otherwise. Scholars' goals are matched to questions by this, not by order,
        /// so reordering the journal no longer mixes goals up.
        /// </summary>
        public SkillType? SkillType { get; set; }
    }
}
