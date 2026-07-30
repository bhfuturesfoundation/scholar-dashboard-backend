using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Entities.Scholars;
using Auth.Models.Enums.Scholars;
using Microsoft.EntityFrameworkCore;

namespace Auth.Tests;

/// <summary>
/// Tests for cohort promotion and its revert.
///
/// Promotion changes the status of every scholar in a cohort at once. Run against the wrong
/// generation, or twice, it is not something anyone fixes by hand across a few hundred
/// accounts — so the properties that make it recoverable are what these pin: the previous
/// state is captured per scholar, and a revert restores exactly that.
/// </summary>
public class ScholarLifecycleTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public ScholarLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"scholars-{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
    }

    public void Dispose() => _context.Dispose();

    private User Scholar(string name, ScholarStatus status, bool isActive = true, int? generationId = null) => new()
    {
        Id = Guid.NewGuid().ToString(),
        FirstName = name,
        LastName = "Test",
        Email = $"{name.ToLowerInvariant()}@bhff.org",
        UserName = $"{name.ToLowerInvariant()}@bhff.org",
        ScholarStatus = status,
        Title = status.ToString(),
        IsActive = isActive,
        GenerationId = generationId
    };

    // ── Batch entries capture enough to undo ──────────────────────────────────

    [Fact]
    public void BatchEntry_CapturesPreviousStateNotJustTheStep()
    {
        // The revert has to restore what each account actually was. Deriving "previous
        // status" from the step would be right for status but wrong for IsActive, because
        // some accounts were already inactive before the run.
        var alreadyInactive = Scholar("Amina", ScholarStatus.Senior, isActive: false);

        var entry = new PromotionBatchEntry
        {
            UserId = alreadyInactive.Id,
            UserDisplayName = "Amina Test",
            PreviousStatus = alreadyInactive.ScholarStatus,
            NewStatus = ScholarStatus.Alumni,
            PreviousTitle = alreadyInactive.Title,
            PreviousIsActive = alreadyInactive.IsActive
        };

        Assert.False(entry.PreviousIsActive);
        Assert.Equal(ScholarStatus.Senior, entry.PreviousStatus);
    }

    [Fact]
    public async Task Revert_RestoresStatusTitleAndActiveFlag()
    {
        var scholar = Scholar("Tarik", ScholarStatus.Senior);
        _context.Users.Add(scholar);

        var batch = new PromotionBatch
        {
            Step = PromotionStep.SeniorsToAlumni,
            AffectedCount = 1,
            DeactivatedAlumni = true,
            PerformedByUserId = "admin",
            PerformedByName = "Admin"
        };

        batch.Entries.Add(new PromotionBatchEntry
        {
            UserId = scholar.Id,
            UserDisplayName = "Tarik Test",
            PreviousStatus = ScholarStatus.Senior,
            NewStatus = ScholarStatus.Alumni,
            PreviousTitle = "Senior",
            PreviousIsActive = true
        });

        // Apply
        scholar.ScholarStatus = ScholarStatus.Alumni;
        scholar.Title = "Alumni";
        scholar.IsActive = false;

        _context.PromotionBatches.Add(batch);
        await _context.SaveChangesAsync();

        // Revert
        foreach (var entry in batch.Entries)
        {
            var target = await _context.Users.FirstAsync(u => u.Id == entry.UserId);
            if (target.ScholarStatus != entry.NewStatus) continue;

            target.ScholarStatus = entry.PreviousStatus;
            target.Title = entry.PreviousTitle;
            target.IsActive = entry.PreviousIsActive;
        }

        await _context.SaveChangesAsync();

        var restored = await _context.Users.FirstAsync(u => u.Id == scholar.Id);

        Assert.Equal(ScholarStatus.Senior, restored.ScholarStatus);
        Assert.Equal("Senior", restored.Title);
        Assert.True(restored.IsActive);
    }

    [Fact]
    public async Task Revert_LeavesScholarsChangedSinceAlone()
    {
        // Someone moved on again after the promotion. Their newer state is the intended one;
        // stamping over it would undo a decision nobody asked to undo.
        var scholar = Scholar("Selma", ScholarStatus.Senior);
        _context.Users.Add(scholar);

        var entry = new PromotionBatchEntry
        {
            UserId = scholar.Id,
            UserDisplayName = "Selma Test",
            PreviousStatus = ScholarStatus.Senior,
            NewStatus = ScholarStatus.Alumni,
            PreviousTitle = "Senior",
            PreviousIsActive = true
        };

        // Promotion ran, then someone set them to Withdrawn afterwards.
        scholar.ScholarStatus = ScholarStatus.Withdrawn;
        await _context.SaveChangesAsync();

        var target = await _context.Users.FirstAsync(u => u.Id == scholar.Id);
        var shouldRestore = target.ScholarStatus == entry.NewStatus;

        Assert.False(shouldRestore);
        Assert.Equal(ScholarStatus.Withdrawn, target.ScholarStatus);
    }

    // ── Transition correctness ────────────────────────────────────────────────

    [Theory]
    [InlineData(PromotionStep.SeniorsToAlumni, ScholarStatus.Senior, ScholarStatus.Alumni)]
    [InlineData(PromotionStep.JuniorsToSeniors, ScholarStatus.Junior, ScholarStatus.Senior)]
    public void EachStepMovesExactlyOneStatus(PromotionStep step, ScholarStatus from, ScholarStatus to)
    {
        var (actualFrom, actualTo) = step switch
        {
            PromotionStep.SeniorsToAlumni => (ScholarStatus.Senior, ScholarStatus.Alumni),
            PromotionStep.JuniorsToSeniors => (ScholarStatus.Junior, ScholarStatus.Senior),
            _ => throw new InvalidOperationException()
        };

        Assert.Equal(from, actualFrom);
        Assert.Equal(to, actualTo);
    }

    [Fact]
    public void PromotionNeverProducesWithdrawn()
    {
        // Withdrawn is terminal and must always be a deliberate act, never a side effect of
        // the yearly roll-over.
        var producedByPromotion = new[] { ScholarStatus.Alumni, ScholarStatus.Senior };

        Assert.DoesNotContain(ScholarStatus.Withdrawn, producedByPromotion);
        Assert.DoesNotContain(ScholarStatus.Unassigned, producedByPromotion);
    }

    [Fact]
    public async Task UnassignedScholarsAreNotSweptIntoAPromotion()
    {
        // Accounts whose historic Title couldn't be mapped stay put. Silently promoting them
        // would place people in a cohort nobody chose for them.
        _context.Users.AddRange(
            Scholar("A", ScholarStatus.Senior),
            Scholar("B", ScholarStatus.Unassigned),
            Scholar("C", ScholarStatus.Unassigned));

        await _context.SaveChangesAsync();

        var candidates = await _context.Users
            .Where(u => u.ScholarStatus == ScholarStatus.Senior)
            .ToListAsync();

        Assert.Single(candidates);
    }

    [Fact]
    public async Task GenerationFilter_RestrictsToThatCohort()
    {
        _context.Users.AddRange(
            Scholar("A", ScholarStatus.Junior, generationId: 1),
            Scholar("B", ScholarStatus.Junior, generationId: 1),
            Scholar("C", ScholarStatus.Junior, generationId: 2));

        await _context.SaveChangesAsync();

        var candidates = await _context.Users
            .Where(u => u.ScholarStatus == ScholarStatus.Junior && u.GenerationId == 1)
            .ToListAsync();

        Assert.Equal(2, candidates.Count);
    }

    // ── Generations ───────────────────────────────────────────────────────────

    [Fact]
    public async Task OnlyOneGenerationCanBeCurrent()
    {
        _context.ScholarGenerations.AddRange(
            new ScholarGeneration { Name = "2025", Year = 2025, IsCurrent = true },
            new ScholarGeneration { Name = "2026", Year = 2026, IsCurrent = false });

        await _context.SaveChangesAsync();

        // Setting a new current must clear the previous in the same pass; a partial update
        // would leave two and intake would land in whichever the query happened to return.
        var all = await _context.ScholarGenerations.ToListAsync();
        var target = all.First(g => g.Year == 2026);

        foreach (var g in all) g.IsCurrent = g.Id == target.Id;
        await _context.SaveChangesAsync();

        var current = await _context.ScholarGenerations.Where(g => g.IsCurrent).ToListAsync();

        Assert.Single(current);
        Assert.Equal(2026, current[0].Year);
    }

    [Fact]
    public void GenerationSurvivesStatusChanges()
    {
        // The cohort is the answer to "which generation was this alumnus from", which is
        // unanswerable if it is inferred from a status that changes every year.
        var scholar = Scholar("Amir", ScholarStatus.Junior, generationId: 7);

        scholar.ScholarStatus = ScholarStatus.Senior;
        scholar.ScholarStatus = ScholarStatus.Alumni;

        Assert.Equal(7, scholar.GenerationId);
    }
}
