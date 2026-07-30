namespace Auth.Models.Enums.Scholars
{
    /// <summary>
    /// Where a scholar sits in the programme lifecycle.
    ///
    /// Introduced alongside the existing free-text <c>User.Title</c> rather than replacing
    /// it: Title is displayed in several screens and exports and its historical values are
    /// inconsistent, so it stays as the human label while this drives logic. The two are
    /// kept in sync on every transition.
    /// </summary>
    public enum ScholarStatus
    {
        /// <summary>
        /// Not yet classified. Existing accounts whose Title could not be mapped land here,
        /// and the admin UI surfaces the count so they can be sorted out in bulk rather than
        /// being silently swept into a real cohort.
        /// </summary>
        Unassigned = 0,

        /// <summary>First-year scholar.</summary>
        Junior = 1,

        /// <summary>Second-year scholar.</summary>
        Senior = 2,

        /// <summary>Completed the programme.</summary>
        Alumni = 3,

        /// <summary>
        /// Left before completing. Terminal, and deliberately never produced by bulk
        /// promotion — someone has to set it deliberately.
        /// </summary>
        Withdrawn = 4
    }

    /// <summary>A single step in the yearly cohort roll-over.</summary>
    public enum PromotionStep
    {
        /// <summary>Senior → Alumni. Run first, so the year's leavers are out before juniors move up.</summary>
        SeniorsToAlumni = 1,

        /// <summary>Junior → Senior.</summary>
        JuniorsToSeniors = 2
    }
}
