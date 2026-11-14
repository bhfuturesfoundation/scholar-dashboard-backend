namespace Auth.Models.Enums
{
    /// <summary>
    /// The four goal areas every scholar sets. The number is also the scholar's goal slot,
    /// and each area has exactly one active journal question.
    /// </summary>
    public enum SkillType
    {
        /// <summary>Personal development — a soft skill.</summary>
        Personal = 1,

        /// <summary>Professional development — a hard skill.</summary>
        Professional = 2,

        /// <summary>Teamwork and leadership — an interpersonal skill.</summary>
        TeamworkLeadership = 3,

        /// <summary>Knowledge and skill base.</summary>
        Knowledge = 4
    }
}
