using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Models.Request;
using Auth.Services.Interfaces;
using Auth.Services.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Auth.Tests;

/// <summary>
/// The admin help-desk actions. These are what someone reaches for when a person cannot
/// sign in, so the properties pinned here are the ones that would otherwise surface as
/// "I did what the screen said and they still can't get in".
/// </summary>
public class AdminAccountServiceTests : IDisposable
{
    private const string AdminId = "admin-1";

    private readonly ApplicationDbContext _context;
    private readonly Mock<UserManager<User>> _userManager;
    private readonly Mock<IAccountService> _accountService = new();
    private readonly Mock<IPasswordResetService> _passwordReset = new();
    private readonly Mock<IAuditService> _audit = new();
    private readonly User _scholar;

    public AdminAccountServiceTests()
    {
        _context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"admin-accounts-{Guid.NewGuid()}")
            .Options);

        _scholar = new User
        {
            Id = "scholar-1",
            FirstName = "Amina",
            LastName = "Test",
            Email = "amina@bhff.org",
            UserName = "amina@bhff.org",
            IsActive = true,
            LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10),
            AccessFailedCount = 5
        };
        _context.Users.Add(_scholar);
        _context.SaveChanges();

        _userManager = new Mock<UserManager<User>>(
            new Mock<IUserStore<User>>().Object, null!, null!, null!, null!, null!, null!, null!, null!);

        _userManager.Setup(m => m.FindByIdAsync(_scholar.Id)).ReturnsAsync(_scholar);
        _userManager.Setup(m => m.GetRolesAsync(It.IsAny<User>())).ReturnsAsync(new List<string> { "User" });
        _userManager.Setup(m => m.GeneratePasswordResetTokenAsync(It.IsAny<User>())).ReturnsAsync("token");
        _userManager.Setup(m => m.ResetPasswordAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.UpdateAsync(It.IsAny<User>())).ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.SetLockoutEndDateAsync(It.IsAny<User>(), It.IsAny<DateTimeOffset?>()))
            .Callback<User, DateTimeOffset?>((u, end) => u.LockoutEnd = end)
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.ResetAccessFailedCountAsync(It.IsAny<User>()))
            .Callback<User>(u => u.AccessFailedCount = 0)
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.SetEmailAsync(It.IsAny<User>(), It.IsAny<string>()))
            .Callback<User, string>((u, e) => u.Email = e)
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(m => m.SetUserNameAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
    }

    public void Dispose() => _context.Dispose();

    private AdminAccountService Service() => new(
        _context,
        _userManager.Object,
        _accountService.Object,
        _passwordReset.Object,
        _audit.Object,
        NullLogger<AdminAccountService>.Instance);

    [Fact]
    public async Task TemporaryPassword_ForcesChangeUnlocksAndEndsSessions()
    {
        string? setTo = null;
        _userManager.Setup(m => m.ResetPasswordAsync(_scholar, "token", It.IsAny<string>()))
            .Callback<User, string, string>((_, _, password) => setTo = password)
            .ReturnsAsync(IdentityResult.Success);

        var result = await Service().SetTemporaryPasswordAsync(AdminId, _scholar.Id);

        Assert.Equal(setTo, result.TemporaryPassword);
        Assert.True(_scholar.MustChangePassword);
        Assert.Null(_scholar.LockoutEnd);
        Assert.Equal(0, _scholar.AccessFailedCount);
        _accountService.Verify(a => a.RevokeSessionsAsync(
            _scholar.Id, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TemporaryPassword_IsAuditedWithoutThePassword()
    {
        string? payload = null;
        _audit.Setup(a => a.LogAsync("Admin.TemporaryPasswordSet", AdminId, It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string?, string?, string?>((_, _, p, _) => payload = p)
            .Returns(Task.CompletedTask);

        var result = await Service().SetTemporaryPasswordAsync(AdminId, _scholar.Id);

        Assert.NotNull(payload);
        Assert.Contains(_scholar.Id, payload);
        Assert.DoesNotContain(result.TemporaryPassword, payload);
    }

    [Fact]
    public void TemporaryPassword_MeetsThePasswordPolicy()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var password = TemporaryPassword.Generate();

            Assert.True(password.Length >= 8);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, c => !char.IsLetterOrDigit(c));
        }
    }

    [Fact]
    public async Task TemporaryPassword_RefusesTheAdminsOwnAccount()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => Service().SetTemporaryPasswordAsync(_scholar.Id, _scholar.Id));

        _userManager.Verify(m => m.ResetPasswordAsync(
            It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ResetEmail_FailureIsNotReportedOrAuditedAsSent()
    {
        _passwordReset.Setup(p => p.SendResetLinkAsync(_scholar))
            .ThrowsAsync(new InvalidOperationException("provider down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service().SendPasswordResetEmailAsync(AdminId, _scholar.Id));

        _audit.Verify(a => a.LogAsync(
            "Admin.PasswordResetEmailSent", It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData("", "Test", "amina@bhff.org")]
    [InlineData("Amina", "Test", "not-an-email")]
    [InlineData("Amina", "Test", "")]
    public async Task UpdateAccount_RejectsMissingNameOrBadEmail(string first, string last, string email)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service().UpdateAccountAsync(
            AdminId, _scholar.Id, new AdminUpdateUserRequest { FirstName = first, LastName = last, Email = email }));
    }

    [Fact]
    public async Task UpdateAccount_RefusesAnEmailAnotherAccountUses()
    {
        _userManager.Setup(m => m.FindByEmailAsync("taken@bhff.org"))
            .ReturnsAsync(new User { Id = "someone-else", Email = "taken@bhff.org" });

        await Assert.ThrowsAsync<ConflictException>(() => Service().UpdateAccountAsync(
            AdminId, _scholar.Id,
            new AdminUpdateUserRequest { FirstName = "Amina", LastName = "Test", Email = "taken@bhff.org" }));

        Assert.Equal("amina@bhff.org", _scholar.Email);
    }

    [Fact]
    public async Task UpdateAccount_MovesEmailAndSignInNameTogether()
    {
        await Service().UpdateAccountAsync(
            AdminId, _scholar.Id,
            new AdminUpdateUserRequest { FirstName = "Amina", LastName = "Test", Email = "amina.new@bhff.org" });

        _userManager.Verify(m => m.SetEmailAsync(_scholar, "amina.new@bhff.org"), Times.Once);
        _userManager.Verify(m => m.SetUserNameAsync(_scholar, "amina.new@bhff.org"), Times.Once);
    }

    [Fact]
    public async Task GetAccount_ReportsOnlyLiveLockoutsAndSessions()
    {
        var now = DateTime.UtcNow;
        _context.RefreshTokens.AddRange(
            new RefreshToken { Token = "live", UserId = _scholar.Id, CreatedAt = now.AddHours(-1), ExpiryTime = now.AddDays(1) },
            new RefreshToken { Token = "revoked", UserId = _scholar.Id, CreatedAt = now.AddMinutes(-5), ExpiryTime = now.AddDays(1), RevokedAt = now },
            new RefreshToken { Token = "expired", UserId = _scholar.Id, CreatedAt = now.AddDays(-9), ExpiryTime = now.AddDays(-2) });
        await _context.SaveChangesAsync();

        var locked = await Service().GetAccountAsync(_scholar.Id);

        Assert.NotNull(locked.LockedOutUntil);
        Assert.Equal(1, locked.ActiveSessions);
        Assert.Equal(now.AddMinutes(-5), locked.LastSignInAt!.Value, TimeSpan.FromSeconds(1));

        _scholar.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _context.SaveChangesAsync();

        var expired = await Service().GetAccountAsync(_scholar.Id);

        Assert.Null(expired.LockedOutUntil);
    }

    [Fact]
    public void ResetLink_UsesConfiguredFrontendAndEscapesTheAddress()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FRONTEND_BASE_URL"] = "https://portal.example.org/" })
            .Build();

        var service = new PasswordResetService(_userManager.Object, Mock.Of<IResendService>(), configuration);

        var link = service.BuildResetLink("amina+test@bhff.org", "a+b/c=");

        Assert.Equal(
            "https://portal.example.org/reset-password?email=amina%2Btest%40bhff.org&token=a%2Bb%2Fc%3D",
            link);
    }
}
