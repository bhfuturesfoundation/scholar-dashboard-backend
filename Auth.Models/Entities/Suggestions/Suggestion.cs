using Auth.Models.Enums.Suggestions;

namespace Auth.Models.Entities.Suggestions
{
    /// <summary>
    /// One note on the suggestion board — something a scholar thinks the programme should
    /// do differently.
    ///
    /// The board is deliberately a shared, visible space rather than a form that posts into
    /// a staff inbox. A suggestion nobody else can see gets made twice by two people who
    /// never learn they agreed, and the person who wrote it has no way to know it was read.
    /// Making them public, votable and status-tracked turns "we should…" into something with
    /// an outcome attached.
    /// </summary>
    public class Suggestion
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        /// <summary>
        /// The author's name captured at write time.
        ///
        /// Denormalised on purpose. A suggestion is a statement someone made at a moment, and
        /// the board shows a history — if a scholar later changes their display name, or
        /// leaves and their account is deactivated, the note should still read the way it did
        /// when it was written. Joining live to Users would rewrite the past, the same trap
        /// the journal answers had before question text was snapshotted.
        /// </summary>
        public string AuthorName { get; set; } = string.Empty;

        /// <summary>
        /// Hides the author's name in the UI. The user id is still stored — anonymity here
        /// means "not shown to peers", not "untraceable", and pretending otherwise would be
        /// dishonest about what moderation can see.
        /// </summary>
        public bool IsAnonymous { get; set; }

        public string Body { get; set; } = string.Empty;

        /// <summary>Index into the client's paper palette. Purely cosmetic.</summary>
        public int ColorIndex { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public SuggestionStatus Status { get; set; } = SuggestionStatus.New;

        /// <summary>Staff reply, shown on the note once set.</summary>
        public string? StaffNote { get; set; }

        public DateTime? StatusChangedAt { get; set; }
        public string? StatusChangedByName { get; set; }

        /// <summary>
        /// Moderated out of sight without deleting, so the decision stays auditable and the
        /// author is not left wondering whether it ever saved. Same reasoning as Kudos.
        /// </summary>
        public bool IsHidden { get; set; }

        /// <summary>Denormalised vote count so the board does not aggregate on every read.</summary>
        public int VoteCount { get; set; }

        public ICollection<SuggestionVote> Votes { get; set; } = new List<SuggestionVote>();
    }

    /// <summary>
    /// One person agreeing with one suggestion.
    ///
    /// Upvote only — there is no downvote, for the same reason kudos has no rating. In a
    /// cohort of a few dozen people who all know each other, a mechanism for publicly
    /// disagreeing with a named person's idea does real damage and stops people posting.
    /// </summary>
    public class SuggestionVote
    {
        public int Id { get; set; }

        public int SuggestionId { get; set; }
        public Suggestion Suggestion { get; set; } = null!;

        public string UserId { get; set; } = string.Empty;
        public User User { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
