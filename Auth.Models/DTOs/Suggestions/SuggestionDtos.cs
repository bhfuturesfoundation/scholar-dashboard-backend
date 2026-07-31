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

        // No IsAnonymous. Anonymous posting was removed: on a board of a few dozen people
        // who all know each other it mostly produced ambiguity about who to follow up with,
        // and staff could see the author regardless. Rows written while it existed keep
        // their anonymity on the public board — see SuggestionService.ToDto.
    }

    public class UpdateSuggestionStatusRequest
    {
        public SuggestionStatus Status { get; set; }
        public string? StaffNote { get; set; }
    }

    /// <summary>
    /// One row of the staff table.
    ///
    /// Carries the author even for a note posted anonymously, which the composer always
    /// disclosed ("staff can still see who posted it, for moderation"). Anonymity on this
    /// board means "not shown to your peers", never "untraceable".
    /// </summary>
    public class SuggestionAdminRowDto
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }

        public string AuthorName { get; set; } = string.Empty;
        public string AuthorEmail { get; set; } = string.Empty;
        public string AuthorId { get; set; } = string.Empty;

        /// <summary>True when the author asked not to be named on the public board.</summary>
        public bool PostedAnonymously { get; set; }

        public string Body { get; set; } = string.Empty;
        public SuggestionStatus Status { get; set; }
        public string? StaffNote { get; set; }
        public DateTime? StatusChangedAt { get; set; }
        public string? StatusChangedByName { get; set; }

        public int VoteCount { get; set; }
        public bool IsHidden { get; set; }
    }

    /// <summary>Filters for the staff table. All optional and ANDed.</summary>
    public class SuggestionAdminQuery
    {
        /// <summary>Inclusive lower bound on CreatedAt (UTC).</summary>
        public DateTime? From { get; set; }

        /// <summary>Inclusive upper bound on CreatedAt (UTC).</summary>
        public DateTime? To { get; set; }

        public SuggestionStatus? Status { get; set; }

        /// <summary>Case-insensitive match against the body or the author's name.</summary>
        public string? Search { get; set; }

        public bool IncludeHidden { get; set; } = true;

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class SuggestionAdminPageDto
    {
        public List<SuggestionAdminRowDto> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }

        /// <summary>Counts per status across the whole filtered set, not just this page.</summary>
        public Dictionary<string, int> StatusCounts { get; set; } = new();
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
