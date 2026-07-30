namespace Auth.Models.DTOs
{
    public class JournalSubmissionDto
    {
        public string MonthYear { get; set; } = string.Empty;

        /// <summary>
        /// Authoritative submission flag, read from <c>JournalSubmissions</c>.
        ///
        /// The overview grid used to infer "submitted" from the presence of an answer to
        /// question 16, which disagreed with the per-scholar detail page (that reads
        /// JournalSubmissions). A scholar could show a green tick in the overview and
        /// "Missing" in the detail view for the same month. Both now read the same source.
        /// </summary>
        public bool Submitted { get; set; }

        /// <summary>0-100, derived from the overall-satisfaction answer. Null when unanswered.</summary>
        public int? SatisfactionScore { get; set; }
    }
}
