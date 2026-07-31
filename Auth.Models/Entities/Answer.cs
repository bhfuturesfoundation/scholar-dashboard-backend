namespace Auth.Models.Entities
{
    public class Answer
    {
        public int AnswerId { get; set; }

        public string ScholarId { get; set; } = string.Empty;
        public bool IsSubmitted { get; set; } = false;
        public DateTime? SubmittedAt { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public User Scholar { get; set; }
        public int QuestionId { get; set; }
        public Question Question { get; set; }
        public string MonthYear { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;

        /// <summary>
        /// The question exactly as it was worded when this answer was written.
        ///
        /// Without it, an answer is only a foreign key to a mutable row: an admin edits the
        /// wording of a question and every historical answer silently re-attaches to the new
        /// text. A scholar's journal from two years ago then shows their old answer under a
        /// question they were never asked, and any trend built on it is comparing different
        /// things. Snapshotting makes the record immutable in the way a journal entry has to
        /// be.
        ///
        /// Nullable because rows written before this existed have no snapshot; readers fall
        /// back to the live question, which is the best available answer for those.
        /// </summary>
        public string? QuestionTextSnapshot { get; set; }

        /// <summary>
        /// Question type at write time. Snapshotted for the same reason — changing a question
        /// from a numeric rating to free text would otherwise make old numeric answers render
        /// and aggregate as though they had always been text.
        /// </summary>
        public string? QuestionTypeSnapshot { get; set; }
    }
}
