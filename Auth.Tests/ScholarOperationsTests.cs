using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Entities.Scholars;
using Auth.Models.Enums;
using Auth.Models.Enums.Scholars;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.Notifications;
using Auth.Services.Services;
using Auth.Services.Services.Resources;
using Auth.Services.Services.Scholars;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Auth.Tests;

/// <summary>
/// Several mentors per scholar, goal areas on questions, permanent deletion, and staff
/// uploads: the rules that would cost data or privacy if they quietly stopped holding.
/// </summary>
public class ScholarOperationsTests : IDisposable
{
    private readonly ApplicationDbContext _context = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"scholar-ops-{Guid.NewGuid()}")
        .Options);

    private readonly Mock<UserManager<User>> _userManager = new(
        new Mock<IUserStore<User>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);

    public ScholarOperationsTests()
    {
        _userManager.Setup(m => m.IsInRoleAsync(It.IsAny<User>(), "Mentor")).ReturnsAsync(true);
    }

    public void Dispose() => _context.Dispose();

    private User AddUser(string id, ScholarStatus status = ScholarStatus.Junior, bool active = true)
    {
        var user = new User { Id = id, FirstName = id, LastName = "Test", Email = $"{id}@bhff.org", UserName = $"{id}@bhff.org", ScholarStatus = status, IsActive = active };
        _context.Users.Add(user);
        _context.SaveChanges();
        return user;
    }

    private MentorAssignmentService Mentors() =>
        new(_context, _userManager.Object, NullLogger<MentorAssignmentService>.Instance);

    // ── Mentors ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Assign_AllowsTwoMentorsAndRefusesAThird()
    {
        AddUser("scholar");
        AddUser("m1"); AddUser("m2"); AddUser("m3");

        await Mentors().AssignAsync("scholar", "m1");
        await Mentors().AssignAsync("scholar", "m2");

        await Assert.ThrowsAsync<ValidationException>(() => Mentors().AssignAsync("scholar", "m3"));
        Assert.Equal(2, _context.MentorAssignments.Count(a => a.ScholarId == "scholar"));
    }

    [Fact]
    public async Task Assign_SameMentorTwiceIsHarmless()
    {
        AddUser("scholar"); AddUser("m1");

        await Mentors().AssignAsync("scholar", "m1");
        await Mentors().AssignAsync("scholar", "m1");

        Assert.Single(_context.MentorAssignments);
    }

    [Fact]
    public async Task Unassign_RemovesOnlyTheNamedMentor()
    {
        AddUser("scholar"); AddUser("m1"); AddUser("m2");
        await Mentors().AssignAsync("scholar", "m1");
        await Mentors().AssignAsync("scholar", "m2");

        await Mentors().UnassignAsync("scholar", "m1");

        Assert.Equal("m2", _context.MentorAssignments.Single().MentorId);
    }

    [Fact]
    public async Task JournalAccess_IsGrantedPerMentor()
    {
        AddUser("scholar"); AddUser("m1"); AddUser("m2");
        await Mentors().AssignAsync("scholar", "m1");
        await Mentors().AssignAsync("scholar", "m2");

        var service = new MentorMenteeService(_context, _userManager.Object, NullLogger<MentorMenteeService>.Instance);
        await service.UpdateMentorJournalAccessAsync("scholar", true, "m1");

        Assert.True(await service.CanMentorAccessJournalAsync("m1", "scholar"));
        Assert.False(await service.CanMentorAccessJournalAsync("m2", "scholar"));
        Assert.False(await service.CanMentorAccessJournalAsync("stranger", "scholar"));
    }

    // ── Goal areas on questions ──────────────────────────────────────────────

    [Fact]
    public async Task SkillQuestion_NeedsAGoalArea()
    {
        var service = new QuestionService(_context, NullLogger<QuestionService>.Instance);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateQuestionAsync(
            new Question { Text = "Q", Order = 1, IsSkill = true }));
    }

    [Fact]
    public async Task SkillQuestion_OneActivePerGoalArea()
    {
        var service = new QuestionService(_context, NullLogger<QuestionService>.Instance);
        await service.CreateQuestionAsync(new Question { Text = "A", Order = 1, IsSkill = true, SkillType = SkillType.Personal });

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateQuestionAsync(
            new Question { Text = "B", Order = 2, IsSkill = true, SkillType = SkillType.Personal }));
    }

    [Fact]
    public async Task NonSkillQuestion_DropsAnyGoalArea()
    {
        var service = new QuestionService(_context, NullLogger<QuestionService>.Instance);

        var created = await service.CreateQuestionAsync(
            new Question { Text = "Plain", Order = 3, IsSkill = false, SkillType = SkillType.Knowledge });

        Assert.Null(created.SkillType);
    }

    [Fact]
    public async Task Goals_FollowTheGoalAreaNotTheQuestionOrder()
    {
        // Personal is at position 7 now; slot 1 must still find it.
        _context.Questions.Add(new Question { QuestionId = 70, Text = "Personal", Order = 7, IsSkill = true, SkillType = SkillType.Personal });
        _context.Questions.Add(new Question { QuestionId = 10, Text = "Something else", Order = 1, IsSkill = false });
        await _context.SaveChangesAsync();
        AddUser("scholar");

        var service = new SkillService(_context, NullLogger<SkillService>.Instance);
        await service.AddOrUpdateSkillAsync("scholar", 1, "Speak up in meetings");

        Assert.Equal(70, _context.Skills.Single().QuestionId);
    }

    // ── Permanent deletion ───────────────────────────────────────────────────

    private AdminAccountService Accounts()
    {
        _userManager.Setup(m => m.GetUsersInRoleAsync("Admin")).ReturnsAsync(new List<User> { new() { Id = "admin" } });
        return new AdminAccountService(
            _context, _userManager.Object, Mock.Of<IAccountService>(), Mock.Of<IPasswordResetService>(),
            Mock.Of<IAuditService>(), NullLogger<AdminAccountService>.Instance);
    }

    [Fact]
    public async Task Delete_RefusesActiveCurrentScholars()
    {
        AddUser("current", ScholarStatus.Senior);

        var result = await Accounts().DeleteAccountsAsync("admin", new[] { "current" });

        Assert.Equal(0, result.DeletedCount);
        Assert.Contains("Deactivate it first", result.Skipped.Single().Reason);
    }

    [Fact]
    public async Task Delete_RefusesAdminsAndTheCaller()
    {
        AddUser("admin", ScholarStatus.Alumni, active: false);
        AddUser("other-admin", ScholarStatus.Alumni, active: false);
        _userManager.Setup(m => m.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<User> { new() { Id = "admin" }, new() { Id = "other-admin" } });

        var service = new AdminAccountService(
            _context, _userManager.Object, Mock.Of<IAccountService>(), Mock.Of<IPasswordResetService>(),
            Mock.Of<IAuditService>(), NullLogger<AdminAccountService>.Instance);

        var result = await service.DeleteAccountsAsync("admin", new[] { "admin", "other-admin" });

        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(2, result.Skipped.Count);
    }

    // ── Resources ────────────────────────────────────────────────────────────

    private ResourceDocumentService Resources() =>
        new(_context, Mock.Of<INotificationService>(), Mock.Of<IAuditService>());

    [Fact]
    public async Task Upload_RejectsAPageRenamedToPdf()
    {
        var html = "<html><script>alert(1)</script></html>"u8.ToArray();

        await Assert.ThrowsAsync<ValidationException>(() => Resources().UploadAsync(
            "admin", "Fake", null, "fake.pdf", new MemoryStream(html), html.Length,
            publish: true, notifyScholars: false, alsoEmail: false));
    }

    [Theory]
    [InlineData("page.html")]
    [InlineData("image.svg")]
    [InlineData("script.exe")]
    public async Task Upload_RejectsTypesThatCouldRun(string fileName)
    {
        var bytes = "%PDF-1.7"u8.ToArray();

        await Assert.ThrowsAsync<ValidationException>(() => Resources().UploadAsync(
            "admin", "Doc", null, fileName, new MemoryStream(bytes), bytes.Length,
            publish: true, notifyScholars: false, alsoEmail: false));
    }

    [Fact]
    public async Task Upload_StoresAPdfAndServesItsOwnType()
    {
        var bytes = "%PDF-1.7 test"u8.ToArray();

        var document = await Resources().UploadAsync(
            "admin", "Guide", "For everyone", "../../etc/guide.pdf", new MemoryStream(bytes), bytes.Length,
            publish: true, notifyScholars: false, alsoEmail: false);

        var file = await Resources().GetFileAsync(document.Id, includeUnpublished: false);

        Assert.NotNull(file);
        Assert.Equal("application/pdf", file!.ContentType);
        Assert.Equal("guide.pdf", file.FileName);
    }

    [Fact]
    public async Task Unpublished_IsHiddenFromScholars()
    {
        var bytes = "%PDF-1.7"u8.ToArray();
        var document = await Resources().UploadAsync(
            "admin", "Draft", null, "draft.pdf", new MemoryStream(bytes), bytes.Length,
            publish: false, notifyScholars: false, alsoEmail: false);

        Assert.Empty(await Resources().ListAsync(includeUnpublished: false));
        Assert.Null(await Resources().GetFileAsync(document.Id, includeUnpublished: false));
        Assert.Single(await Resources().ListAsync(includeUnpublished: true));
    }

    [Fact]
    public void TemporaryPassword_NeverStartsWithASymbol()
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Assert.True(char.IsLetterOrDigit(TemporaryPassword.Generate()[0]));
        }
    }
}
