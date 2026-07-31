using Auth.Models.DTOs.Engagement;

namespace Auth.Services.Interfaces.Engagement
{
    /// <summary>
    /// What a scholar sees about their own progress: journal trend, streak, an anonymous
    /// cohort comparison, and badges.
    ///
    /// Every method is scoped to one scholar id supplied by the caller from the authenticated
    /// principal — there is deliberately no "get any scholar's progress" overload, so this
    /// service cannot become a way to read someone else's satisfaction scores.
    /// </summary>
    public interface IScholarProgressService
    {
        Task<ScholarProgressDto> GetProgressAsync(string scholarId, CancellationToken cancellationToken = default);

        Task<List<AchievementDto>> GetAchievementsAsync(string scholarId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Re-checks every rule and awards anything newly earned. Idempotent — a badge is
        /// only ever awarded once. Returns how many were new.
        /// </summary>
        Task<int> EvaluateAsync(string scholarId, CancellationToken cancellationToken = default);

        /// <summary>Acknowledges new badges so they are celebrated once, not every load.</summary>
        Task MarkAchievementsSeenAsync(string scholarId, CancellationToken cancellationToken = default);
    }
}
