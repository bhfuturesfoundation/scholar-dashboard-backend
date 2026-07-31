using Auth.Models.Entities;
using Auth.Services.Services;

namespace Auth.Tests;

/// <summary>
/// Tests for the question snapshot on journal answers.
///
/// The bug these guard against: an answer stored only a foreign key to a mutable question
/// row, so editing a question's wording silently re-attached every historical answer to the
/// new text. A scholar's journal from two years ago then showed their words under a question
/// they were never asked, and any trend built on it compared different things.
/// </summary>
public class AnswerSnapshotTests
{
    private static Question Question(int id, string text, string type = "Text") =>
        new() { QuestionId = id, Text = text, Type = type, Active = true };

    [Fact]
    public void ApplyQuestionSnapshot_CapturesTextAndType()
    {
        var answer = new Answer { QuestionId = 7, Response = "It went well." };

        answer.ApplyQuestionSnapshot(Question(7, "How are your studies going?", "Text"));

        Assert.Equal("How are your studies going?", answer.QuestionTextSnapshot);
        Assert.Equal("Text", answer.QuestionTypeSnapshot);
    }

    [Fact]
    public void EditingTheQuestionAfterwards_DoesNotChangeTheSnapshot()
    {
        // The headline regression, expressed directly.
        var question = Question(7, "How are your studies going?");
        var answer = new Answer { QuestionId = 7, Response = "It went well." };

        answer.ApplyQuestionSnapshot(question);

        question.Text = "Describe your academic progress this month.";

        Assert.Equal("How are your studies going?", answer.QuestionTextSnapshot);
        Assert.Equal("How are your studies going?", answer.ResolveQuestionText(question));
    }

    [Fact]
    public void ResolveQuestionText_FallsBackToLiveQuestionWhenNoSnapshot()
    {
        // Rows written before snapshotting existed have no snapshot. The live question is
        // the best available answer for them, not an error.
        var answer = new Answer { QuestionId = 7, Response = "Fine." };

        Assert.Equal("How are your studies going?",
            answer.ResolveQuestionText(Question(7, "How are your studies going?")));
    }

    [Fact]
    public void ResolveQuestionText_ReturnsEmptyWhenNeitherExists()
    {
        // A question deleted outright, answered before snapshotting. Must not throw.
        var answer = new Answer { QuestionId = 99, Response = "Something." };

        Assert.Equal(string.Empty, answer.ResolveQuestionText(null));
        Assert.Equal("Text", answer.ResolveQuestionType(null));
    }

    [Fact]
    public void ResolveQuestionType_PrefersSnapshot()
    {
        // Changing a rating question to free text would otherwise make old numeric answers
        // render and aggregate as though they had always been text.
        var answer = new Answer { QuestionId = 1, Response = "8" };
        answer.ApplyQuestionSnapshot(Question(1, "Rate your satisfaction", "small"));

        var edited = Question(1, "Rate your satisfaction", "Text");

        Assert.Equal("small", answer.ResolveQuestionType(edited));
    }

    [Fact]
    public void ApplyQuestionSnapshot_WithNullQuestion_LeavesSnapshotUnset()
    {
        // Callers pass whatever the lookup found; a miss must be harmless rather than
        // stamping empty strings over a good snapshot.
        var answer = new Answer { QuestionId = 7 };
        answer.ApplyQuestionSnapshot(Question(7, "Original"));

        answer.ApplyQuestionSnapshot(null);

        Assert.Equal("Original", answer.QuestionTextSnapshot);
    }

    [Fact]
    public void ApplyQuestionSnapshots_StampsABatchFromOneLookup()
    {
        var questions = new Dictionary<int, Question>
        {
            [1] = Question(1, "First question"),
            [2] = Question(2, "Second question"),
        };

        var answers = new List<Answer>
        {
            new() { QuestionId = 1 },
            new() { QuestionId = 2 },
            new() { QuestionId = 3 },   // no matching question
        };

        answers.ApplyQuestionSnapshots(questions);

        Assert.Equal("First question", answers[0].QuestionTextSnapshot);
        Assert.Equal("Second question", answers[1].QuestionTextSnapshot);
        Assert.Null(answers[2].QuestionTextSnapshot);
    }

    [Fact]
    public void BlankSnapshotIsTreatedAsAbsent()
    {
        // An empty string must not shadow the live question — that would show a blank
        // question rather than falling back.
        var answer = new Answer { QuestionId = 1, QuestionTextSnapshot = "   " };

        Assert.Equal("Live text", answer.ResolveQuestionText(Question(1, "Live text")));
    }
}
