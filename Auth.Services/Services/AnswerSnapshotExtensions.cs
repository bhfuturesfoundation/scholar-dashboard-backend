using Auth.Models.Entities;

namespace Auth.Services.Services
{
    /// <summary>
    /// Helpers for capturing and reading the question snapshot on an answer.
    ///
    /// Centralised because there are three separate write paths (draft save, submit via
    /// JournalService, submit via AnswerService) and a snapshot that only some of them
    /// populate is worse than none — the gaps would be invisible until someone edited a
    /// question and found half the history had moved.
    /// </summary>
    public static class AnswerSnapshotExtensions
    {
        /// <summary>
        /// Stamps the question's current wording and type onto an answer.
        /// Safe to call when the question is unknown — the snapshot simply stays null and
        /// readers fall back to the live question.
        /// </summary>
        public static void ApplyQuestionSnapshot(this Answer answer, Question? question)
        {
            if (question is null) return;

            answer.QuestionTextSnapshot = question.Text;
            answer.QuestionTypeSnapshot = question.Type;
        }

        /// <summary>
        /// Stamps snapshots onto a batch, looking each question up once.
        /// </summary>
        public static void ApplyQuestionSnapshots(
            this IEnumerable<Answer> answers, IReadOnlyDictionary<int, Question> questionsById)
        {
            foreach (var answer in answers)
            {
                if (questionsById.TryGetValue(answer.QuestionId, out var question))
                    answer.ApplyQuestionSnapshot(question);
            }
        }

        /// <summary>
        /// The wording to display for an answer: the snapshot when present, otherwise the
        /// live question. Rows written before snapshotting existed fall back, which is the
        /// best available answer for them.
        /// </summary>
        public static string ResolveQuestionText(this Answer answer, Question? liveQuestion) =>
            !string.IsNullOrWhiteSpace(answer.QuestionTextSnapshot)
                ? answer.QuestionTextSnapshot
                : liveQuestion?.Text ?? string.Empty;

        public static string ResolveQuestionType(this Answer answer, Question? liveQuestion) =>
            !string.IsNullOrWhiteSpace(answer.QuestionTypeSnapshot)
                ? answer.QuestionTypeSnapshot
                : liveQuestion?.Type ?? "Text";
    }
}
