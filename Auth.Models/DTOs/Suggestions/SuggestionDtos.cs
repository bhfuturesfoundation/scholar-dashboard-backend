using Auth.Models.Enums.Suggestions;

namespace Auth.Models.DTOs.Suggestions
{
    public class SuggestionDto
    {
        public int Id { get; set; }

        /// <summary>Null when the author chose to post anonymously.</summary>
        public string? AuthorName { get; set; }

        /// <summary>True when the caller wrote this one — the client shows a delete control.</summary>
        public bool IsMine { get; set; }

        public string Body { get; set; } = string.Empty;
        public int ColorIndex { get; set; }
        public DateTime CreatedAt { get; set; }

        public SuggestionStatus Status { get; set; }
        public string? StaffNote { get; set; }
        public DateTime? StatusChangedAt { get; set; }
        public string? StatusChangedByName { get; set; }

        public int VoteCount { get; set; }

        /// <summary>Whether the caller has voted for it.</summary>
        public bool HasVoted { get; set; }
    }

    public class CreateSuggestionRequest
    {
        public string Body { get; set; } = string.Empty;
        public int ColorIndex { get; set; }
        public bool IsAnonymous { get; set; }
    }

    public class UpdateSuggestionStatusRequest
    {
        public SuggestionStatus Status { get; set; }
        public string? StaffNote { get; set; }
    }

    /// <summary>
    /// The board plus what the caller is allowed to do with it, so the client does not have
    /// to re-derive permissions from a role list it may not have loaded yet.
    /// </summary>
    public class SuggestionBoardDto
    {
        public List<SuggestionDto> Items { get; set; } = new();

        /// <summary>True for Admin and Program Manager: may set status and hide notes.</summary>
        public bool CanModerate { get; set; }

        /// <summary>How many more the caller may post today.</summary>
        public int RemainingToday { get; set; }
    }
}
