using Auth.Models.DTOs.Engagement;

namespace Auth.Services.Interfaces.Engagement
{
    /// <summary>
    /// Scholar-to-scholar recognition. Positive-only by design — there is no rating,
    /// downvote or score, because a recognition feature that can be used negatively becomes
    /// a popularity contest with a floor.
    /// </summary>
    public interface IKudosService
    {
        List<KudosCategoryDto> GetCategories();

        /// <summary>
        /// Records recognition. Rejects self-kudos, unknown categories, inactive recipients,
        /// and more than one per recipient per day.
        /// </summary>
        Task<KudosDto> GiveAsync(
            string fromUserId, string toUserId, string category, string? message,
            CancellationToken cancellationToken = default);

        Task<KudosSummaryDto> GetForUserAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>Shared feed of recent recognition across the cohort.</summary>
        Task<List<KudosDto>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default);

        /// <summary>Staff moderation. Hides rather than deletes, so the decision is auditable.</summary>
        Task HideAsync(int kudosId, CancellationToken cancellationToken = default);
    }
}
