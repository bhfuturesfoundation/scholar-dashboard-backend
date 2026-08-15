using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Services.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auth.Tests;

/// <summary>
/// Question.Order decides the sequence scholars answer in, so two active questions claiming
/// the same position leaves the journal order undefined — the database is free to return
/// either first, and need not be consistent between requests.
///
/// Create had always refused a duplicate. Update and reactivate had not, so the way to end
/// up with two questions at position 3 was to create the second at 4 and edit it, or to
/// archive one and restore it after its slot had been reused. These pin all three paths.
///
/// The unique filtered index is the real guarantee; these checks are what turn a constraint
/// violation into a 409 with a sentence an admin can act on. The in-memory provider does not
/// enforce filtered indexes, which is precisely why the service-level checks are worth
/// testing separately — they are what runs first in production too.
/// </summary>
public class QuestionOrderTests
{
    private static QuestionService NewService(params Question[] seed)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"questions-{Guid.NewGuid()}")
            .Options;

        var context = new ApplicationDbContext(options);
        context.Questions.AddRange(seed);
        context.SaveChanges();

        return new QuestionService(context, NullLogger<QuestionService>.Instance);
    }

    private static Question Q(int id, int order, bool active = true) => new()
    {
        QuestionId = id,
        Text = $"Question {id}",
        Type = "text",
        Order = order,
        Active = active,
    };

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_RejectsAnOrderAnActiveQuestionAlreadyHolds()
    {
        var service = NewService(Q(1, 3));

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateQuestionAsync(Q(0, 3)));
    }

    [Fact]
    public async Task Create_AllowsAnOrderOnlyAnArchivedQuestionHolds()
    {
        // The archive is not rendered in sequence, so a deactivated question sitting at 3 is
        // not a reason to refuse a new active one there.
        var service = NewService(Q(1, 3, active: false));

        var created = await service.CreateQuestionAsync(Q(0, 3));

        Assert.Equal(3, created.Order);
    }

    [Fact]
    public async Task Create_AllowsAFreeOrder()
    {
        var service = NewService(Q(1, 1), Q(2, 2));

        var created = await service.CreateQuestionAsync(Q(0, 3));

        Assert.Equal(3, created.Order);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_RejectsMovingOntoAnotherActiveQuestionsOrder()
    {
        // The hole this closes: create at 4, then edit to 3.
        var service = NewService(Q(1, 3), Q(2, 4));

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateQuestionAsync(2, Q(2, 3)));
    }

    [Fact]
    public async Task Update_AllowsSavingAQuestionWithoutMovingIt()
    {
        // Excluding itself matters: editing only the text must not read as a self-collision.
        var service = NewService(Q(1, 3), Q(2, 4));

        var edited = Q(2, 4);
        edited.Text = "Reworded";

        await service.UpdateQuestionAsync(2, edited);

        var stored = (await service.GetActiveQuestionsAsync()).Single(q => q.QuestionId == 2);
        Assert.Equal("Reworded", stored.Text);
        Assert.Equal(4, stored.Order);
    }

    [Fact]
    public async Task Update_AllowsMovingToAFreeOrder()
    {
        var service = NewService(Q(1, 3), Q(2, 4));

        await service.UpdateQuestionAsync(2, Q(2, 7));

        var stored = (await service.GetActiveQuestionsAsync()).Single(q => q.QuestionId == 2);
        Assert.Equal(7, stored.Order);
    }

    [Fact]
    public async Task Update_IgnoresTheClashWhenTheQuestionIsBeingArchived()
    {
        // Deactivating something that happens to share an order is not a conflict — it is
        // leaving the sequence, not joining it.
        var service = NewService(Q(1, 3), Q(2, 4));

        await service.UpdateQuestionAsync(2, Q(2, 3, active: false));

        var archived = (await service.GetInactiveQuestionsAsync()).Single();
        Assert.Equal(2, archived.QuestionId);
    }

    // ── Reactivate ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reactivate_RejectsRestoringIntoATakenOrder()
    {
        // Archive the question at 3, give 3 to something else, then restore the first.
        var service = NewService(Q(1, 3, active: false), Q(2, 3));

        await Assert.ThrowsAsync<ConflictException>(() => service.ReactivateQuestionAsync(1));
    }

    [Fact]
    public async Task Reactivate_RestoresWhenTheOrderIsStillFree()
    {
        var service = NewService(Q(1, 3, active: false), Q(2, 4));

        await service.ReactivateQuestionAsync(1);

        var active = await service.GetActiveQuestionsAsync();
        Assert.Contains(active, q => q.QuestionId == 1 && q.Order == 3);
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ActiveQuestions_ComeBackInOrder()
    {
        var service = NewService(Q(1, 5), Q(2, 1), Q(3, 3));

        var orders = (await service.GetActiveQuestionsAsync()).Select(q => q.Order).ToList();

        Assert.Equal(new[] { 1, 3, 5 }, orders);
    }
}
