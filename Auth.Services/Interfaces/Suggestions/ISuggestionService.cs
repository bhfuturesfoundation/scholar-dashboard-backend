using Auth.Models.DTOs.Suggestions;

namespace Auth.Services.Interfaces.Suggestions
{
    /// <summary>
    /// The suggestion board.
    ///
    /// Public and votable rather than a form into a staff inbox: a suggestion nobody else
    /// can see gets made twice by two people who never learn they agreed, and the person who
    /// wrote it never finds out whether it was read.
    /// </summary>
    public interface ISuggestionService
    {
        Task<SuggestionBoardDto> GetBoardAsync(
            string userId, bool canModerate, CancellationToken cancellationToken = default);

        Task<SuggestionDto> CreateAsync(
            string userId, string authorName, CreateSuggestionRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>Authors may withdraw their own; moderators may remove any.</summary>
        Task<bool> DeleteAsync(
            string userId, int id, bool canModerate, CancellationToken cancellationToken = default);

        /// <summary>Adds the caller's vote, or removes it if they had already voted.</summary>
        Task<SuggestionDto> ToggleVoteAsync(
            string userId, int id, CancellationToken cancellationToken = default);

        /// <summary>Staff only. Notifies the author when the status actually changes.</summary>
        Task<SuggestionDto> SetStatusAsync(
            int id, UpdateSuggestionStatusRequest request, string staffName,
            CancellationToken cancellationToken = default);

        /// <summary>Hides rather than deletes, so a moderation decision stays auditable.</summary>
        Task<bool> SetHiddenAsync(int id, bool hidden, CancellationToken cancellationToken = default);
    }
}
