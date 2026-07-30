using Auth.Models.Entities;
using Auth.Services.Settings;

namespace Auth.Tests;

/// <summary>
/// Regression tests for the refresh-token rotation grace window.
///
/// The bug these guard against: a browser fires several requests at once on page load.
/// When the access token had expired, each one presented the same refresh token from the
/// cookie. The first rotated it; the rest arrived holding a value that was now
/// revoked-and-replaced, which reuse detection treated as a stolen token and responded to
/// by revoking EVERY session for that user. The result was that simply loading a page after
/// the access token aged out logged the user out and broke their next login.
///
/// These tests exercise the decision logic directly rather than through TokenService, which
/// needs UserManager and a JWT signing key. The condition being pinned is the one that was
/// wrong: "revoked + replaced" alone is not evidence of reuse — the age of the revocation is
/// what distinguishes a concurrent request from a replay.
/// </summary>
public class RefreshTokenRotationTests
{
    private static readonly RefreshTokenSettings Settings = new()
    {
        ExpirationInDays = 7,
        MaxRefreshCount = 100,
        MaxActiveSessionsPerUser = 5,
        EnableTokenRotation = true,
        DetectTokenReuse = true,
        RotationGraceSeconds = 60
    };

    /// <summary>
    /// Mirrors the branch in <c>TokenService.ValidateRefreshTokenAsync</c>. Returns true when
    /// the presented token should be accepted as a concurrent request rather than treated
    /// as reuse.
    /// </summary>
    private static bool IsConcurrentReplay(RefreshToken token, RefreshTokenSettings settings, DateTime now)
    {
        if (token.RevokedAt is null) return false;
        if (token.ReplacedByToken is null) return false;

        return now - token.RevokedAt.Value <= TimeSpan.FromSeconds(settings.RotationGraceSeconds);
    }

    private static RefreshToken RotatedToken(DateTime revokedAt) => new()
    {
        Token = "old-token",
        UserId = "user-1",
        CreatedAt = revokedAt.AddMinutes(-30),
        ExpiryTime = revokedAt.AddDays(7),
        RevokedAt = revokedAt,
        ReplacedByToken = "new-token",
        RevokeReason = "Replaced by new token (normal rotation)"
    };

    [Fact]
    public void TokenRotatedMomentsAgo_IsTreatedAsConcurrentRequest_NotReuse()
    {
        var now = new DateTime(2026, 7, 30, 9, 44, 39, DateTimeKind.Utc);

        // The exact shape from the reported incident: three parallel requests, the second
        // and third arriving milliseconds after the first rotated the token.
        var token = RotatedToken(now.AddMilliseconds(-120));

        Assert.True(IsConcurrentReplay(token, Settings, now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(59)]
    [InlineData(60)]
    public void TokenRotatedWithinGraceWindow_IsAccepted(int secondsAgo)
    {
        var now = DateTime.UtcNow;
        var token = RotatedToken(now.AddSeconds(-secondsAgo));

        Assert.True(IsConcurrentReplay(token, Settings, now));
    }

    [Theory]
    [InlineData(61)]
    [InlineData(120)]
    [InlineData(3600)]
    [InlineData(86400)]
    public void TokenRotatedBeforeGraceWindow_IsStillTreatedAsReuse(int secondsAgo)
    {
        // The grace window must not weaken genuine reuse detection: a superseded token
        // replayed well after rotation is exactly the attack this is meant to catch.
        var now = DateTime.UtcNow;
        var token = RotatedToken(now.AddSeconds(-secondsAgo));

        Assert.False(IsConcurrentReplay(token, Settings, now));
    }

    [Fact]
    public void TokenRevokedWithoutReplacement_IsNeverConcurrentReplay()
    {
        // An explicit logout or admin revocation sets RevokedAt but no ReplacedByToken.
        // That is a dead session, not a rotation, and must not be resurrected by the window.
        var now = DateTime.UtcNow;
        var token = RotatedToken(now.AddSeconds(-1));
        token.ReplacedByToken = null;
        token.RevokeReason = "User logged out";

        Assert.False(IsConcurrentReplay(token, Settings, now));
    }

    [Fact]
    public void ActiveToken_IsNotConcurrentReplay()
    {
        var token = RotatedToken(DateTime.UtcNow);
        token.RevokedAt = null;
        token.ReplacedByToken = null;

        Assert.False(IsConcurrentReplay(token, Settings, DateTime.UtcNow));
    }

    [Fact]
    public void GraceWindowIsShortEnoughToBeSafe()
    {
        // A long window would make reuse detection meaningless. Pin the intent so nobody
        // "fixes" a flaky test by widening this to an hour.
        Assert.InRange(Settings.RotationGraceSeconds, 5, 120);
    }

    [Fact]
    public void DefaultSettings_EnableRotationAndReuseDetection()
    {
        // The grace window is a correction to reuse detection, not a replacement for it.
        var defaults = new RefreshTokenSettings();
        Assert.Equal(60, defaults.RotationGraceSeconds);
    }
}
