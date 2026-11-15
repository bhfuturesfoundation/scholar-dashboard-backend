using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Services.Services;
using Auth.Services.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;

namespace Auth.Tests;

/// <summary>
/// A sign-in must end eventually, however often it is refreshed.
///
/// The bug: every rotation minted a refresh token with a fresh 7-day expiry, and the
/// MaxRefreshCount meant to stop that is counted per token, so it reset to zero on every
/// rotation. An always-open tab renewed itself indefinitely — one mentor's session ran for
/// a month and showed him as online the whole time. These go through the real TokenService.
/// </summary>
public class SessionLifetimeTests
{
    private const int SessionDays = 30;
    private const int SlidingDays = 7;

    private static (TokenService Service, ApplicationDbContext Db, User User) Build()
    {
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var user = new User { Id = "user-1", Email = "scholar@example.com", UserName = "scholar@example.com" };

        var userManager = new Mock<UserManager<User>>(
            Mock.Of<IUserStore<User>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        userManager.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);
        userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "User" });
        userManager.Setup(m => m.GetClaimsAsync(user)).ReturnsAsync(new List<Claim>());

        var service = new TokenService(
            userManager.Object,
            db,
            Options.Create(new JWTSettings
            {
                Secret = "test-secret-key-that-is-long-enough-for-hmac-sha256!!",
                Issuer = "test",
                Audience = "test",
                ExpirationInMinutes = 60,
                RefreshTokenExpirationInDays = SlidingDays,
            }),
            Options.Create(new RefreshTokenSettings
            {
                ExpirationInDays = SlidingDays,
                MaxRefreshCount = 100,
                MaxActiveSessionsPerUser = 0,
                EnableTokenRotation = true,
                DetectTokenReuse = true,
                AbsoluteSessionLifetimeDays = SessionDays,
            }),
            NullLogger<TokenService>.Instance);

        return (service, db, user);
    }

    [Fact]
    public async Task NewSignIn_GetsSessionDeadline_AndSlidingExpiryWithinIt()
    {
        var (service, db, user) = Build();

        var token = await service.GenerateRefreshTokenAsync(user);
        var row = await db.RefreshTokens.SingleAsync(t => t.Token == token);

        Assert.NotNull(row.SessionExpiresAt);
        Assert.InRange(row.SessionExpiresAt!.Value, DateTime.UtcNow.AddDays(SessionDays - 1), DateTime.UtcNow.AddDays(SessionDays));
        Assert.InRange(row.ExpiryTime, DateTime.UtcNow.AddDays(SlidingDays - 1), DateTime.UtcNow.AddDays(SlidingDays));
    }

    [Fact]
    public async Task Rotation_InheritsDeadline_InsteadOfExtendingIt()
    {
        var (service, db, user) = Build();
        var first = await service.GenerateRefreshTokenAsync(user);
        var deadline = (await db.RefreshTokens.SingleAsync(t => t.Token == first)).SessionExpiresAt;

        var rotated = await service.RotateRefreshTokenAsync("ignored", first, user.Id);
        var row = await db.RefreshTokens.SingleAsync(t => t.Token == rotated.RefreshToken);

        Assert.Equal(deadline, row.SessionExpiresAt);
    }

    [Fact]
    public async Task Rotation_NearDeadline_ClampsExpiryToTheDeadline()
    {
        var (service, db, user) = Build();
        var first = await service.GenerateRefreshTokenAsync(user);

        // Two hours of session left: the sliding 7 days must not carry past it.
        var row = await db.RefreshTokens.SingleAsync(t => t.Token == first);
        var deadline = DateTime.UtcNow.AddHours(2);
        row.SessionExpiresAt = deadline;
        await db.SaveChangesAsync();

        var rotated = await service.RotateRefreshTokenAsync("ignored", first, user.Id);
        var next = await db.RefreshTokens.SingleAsync(t => t.Token == rotated.RefreshToken);

        Assert.Equal(deadline, next.ExpiryTime);
    }

    [Fact]
    public async Task Rotation_OfTokenFromBeforeDeadlines_StartsTheCapNow()
    {
        var (service, db, user) = Build();

        // A token issued before this change has no deadline. It must get one on its next
        // rotation, not be grandfathered into an endless session.
        db.RefreshTokens.Add(new RefreshToken
        {
            Token = "legacy",
            UserId = user.Id,
            CreatedAt = DateTime.UtcNow.AddDays(-40),
            ExpiryTime = DateTime.UtcNow.AddDays(5),
        });
        await db.SaveChangesAsync();

        var rotated = await service.RotateRefreshTokenAsync("ignored", "legacy", user.Id);
        var next = await db.RefreshTokens.SingleAsync(t => t.Token == rotated.RefreshToken);

        Assert.NotNull(next.SessionExpiresAt);
        Assert.True(next.SessionExpiresAt <= DateTime.UtcNow.AddDays(SessionDays));
    }
}
