namespace Auth.Models.Enums.Suggestions
{
    /// <summary>
    /// Where a suggestion has got to.
    ///
    /// The point of having a status at all is that "we read it and decided not to" is a
    /// real, respectable outcome, and a board without it trains people that suggestions
    /// vanish. <see cref="Declined"/> exists so staff can say no visibly rather than by
    /// silence.
    ///
    /// Persisted as integers — never renumber.
    /// </summary>
    public enum SuggestionStatus
    {
        New = 0,

        /// <summary>Staff have seen it and are thinking about it.</summary>
        UnderReview = 1,

        /// <summary>Agreed, and going to happen.</summary>
        Planned = 2,

        /// <summary>Done.</summary>
        Done = 3,

        /// <summary>Considered and not going ahead. Should carry a staff note explaining why.</summary>
        Declined = 4
    }
}
