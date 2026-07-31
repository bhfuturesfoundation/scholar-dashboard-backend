using Auth.Models.Constants;
using Auth.Models.Entities.Notifications;
using Auth.Models.Enums.Notifications;
using Auth.Services.Interfaces.Notifications;
using Auth.Services.Services.Notifications;

namespace Auth.Tests;

/// <summary>
/// Tests for the preference matrix, quiet hours, and the notification catalogue.
///
/// These are the parts where a mistake is invisible: a preference that fails open sends
/// email somebody switched off, a quiet-hours boundary that is wrong by one wakes a phone
/// at 03:00, and a missing catalogue entry produces an email whose subject is a raw key.
/// None of those throw, and none show up in a smoke test.
/// </summary>
public class NotificationPreferenceTests
{
    private static NotificationPreference Preference(Action<NotificationPreference>? configure = null)
    {
        var preference = new NotificationPreference { TimeZoneId = "UTC" };
        configure?.Invoke(preference);
        return preference;
    }

    // ── Channel gating ────────────────────────────────────────────────────────

    [Fact]
    public void InApp_IsAlwaysAllowed_EvenWhenEverythingElseIsOff()
    {
        // The bell menu is the record of what happened. If it could be muted, an event
        // would exist with no trace anywhere the scholar can reach.
        var preference = Preference(p =>
        {
            p.EmailKudos = false;
            p.PushKudos = false;
        });

        Assert.True(preference.Allows(NotificationCategory.Kudos, NotificationChannel.InApp));
    }

    [Fact]
    public void System_IsAlwaysAllowed_OnEveryChannel()
    {
        // Account and security events are not negotiable — nobody gets to switch off being
        // told their password changed.
        var preference = Preference(p =>
        {
            p.EmailAnnouncements = false;
            p.PushAnnouncements = false;
        });

        Assert.True(preference.Allows(NotificationCategory.System, NotificationChannel.Email));
        Assert.True(preference.Allows(NotificationCategory.System, NotificationChannel.Push));
    }

    [Fact]
    public void MutingACategory_BlocksThatChannelOnly()
    {
        var preference = Preference(p =>
        {
            p.EmailKudos = false;
            p.PushKudos = true;
        });

        Assert.False(preference.Allows(NotificationCategory.Kudos, NotificationChannel.Email));
        Assert.True(preference.Allows(NotificationCategory.Kudos, NotificationChannel.Push));
    }

    [Fact]
    public void MinigameInvites_AreNeverEmailed()
    {
        // An invite expires in three minutes. An email about one is guaranteed to arrive
        // after it is worthless, so there is deliberately no switch that turns this on.
        var preference = Preference();

        Assert.False(preference.Allows(NotificationCategory.Minigame, NotificationChannel.Email));
    }

    [Fact]
    public void Defaults_AreQuietOnPush_AndOnForJournalEmail()
    {
        // Push defaults matter: granting notification permission is not the same as asking
        // for every category, and push is the channel people uninstall you over.
        var preference = Preference();

        Assert.True(preference.Allows(NotificationCategory.Journal, NotificationChannel.Email));
        Assert.False(preference.Allows(NotificationCategory.Achievement, NotificationChannel.Email));
        Assert.False(preference.Allows(NotificationCategory.Kudos, NotificationChannel.Push));
        Assert.True(preference.Allows(NotificationCategory.Journal, NotificationChannel.Push));
    }

    // ── Quiet hours ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(23, true)]   // inside, after the start
    [InlineData(2, true)]    // inside, after midnight — the wrap case
    [InlineData(7, true)]    // inside, last hour
    [InlineData(8, false)]   // the end hour is exclusive
    [InlineData(21, false)]  // before the start
    public void OvernightQuietHours_WrapAroundMidnight(int hourUtc, bool expectedQuiet)
    {
        // 22:00–08:00 spans midnight, so a naive `hour >= start && hour < end` is false for
        // every hour of the night — the exact window it was meant to cover.
        var preference = Preference(p =>
        {
            p.QuietHoursStart = 22;
            p.QuietHoursEnd = 8;
        });

        var instant = new DateTime(2026, 7, 15, hourUtc, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expectedQuiet, preference.IsQuietAt(instant));
    }

    [Theory]
    [InlineData(14, true)]
    [InlineData(12, false)]
    [InlineData(17, false)]
    public void SameDayQuietHours_DoNotWrap(int hourUtc, bool expectedQuiet)
    {
        var preference = Preference(p =>
        {
            p.QuietHoursStart = 13;
            p.QuietHoursEnd = 17;
        });

        var instant = new DateTime(2026, 7, 15, hourUtc, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expectedQuiet, preference.IsQuietAt(instant));
    }

    [Fact]
    public void QuietHoursDisabled_MeansNeverQuiet()
    {
        var preference = Preference(p =>
        {
            p.QuietHoursEnabled = false;
            p.QuietHoursStart = 0;
            p.QuietHoursEnd = 23;
        });

        Assert.False(preference.IsQuietAt(new DateTime(2026, 7, 15, 3, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void EqualStartAndEnd_IsTreatedAsNoQuietHours()
    {
        // Otherwise it reads as either a zero-length window or a 24-hour one, and the
        // 24-hour reading would silently mute somebody's email forever.
        var preference = Preference(p =>
        {
            p.QuietHoursStart = 22;
            p.QuietHoursEnd = 22;
        });

        Assert.False(preference.IsQuietAt(new DateTime(2026, 7, 15, 22, 30, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void UnknownTimeZone_FallsBackToUtc_RatherThanThrowing()
    {
        // A bad preference value must never be able to stop a send.
        var preference = Preference(p =>
        {
            p.TimeZoneId = "Mars/Olympus_Mons";
            p.QuietHoursStart = 22;
            p.QuietHoursEnd = 8;
        });

        var exception = Record.Exception(() =>
            preference.IsQuietAt(new DateTime(2026, 7, 15, 2, 0, 0, DateTimeKind.Utc)));

        Assert.Null(exception);
    }

    [Fact]
    public void NextDeliverableInstant_LandsOutsideQuietHours()
    {
        var preference = Preference(p =>
        {
            p.QuietHoursStart = 22;
            p.QuietHoursEnd = 8;
        });

        var duringTheNight = new DateTime(2026, 7, 15, 1, 0, 0, DateTimeKind.Utc);
        var deliverAt = preference.NextDeliverableInstant(duringTheNight);

        Assert.False(preference.IsQuietAt(deliverAt));
        Assert.True(deliverAt > duringTheNight);
    }

    [Fact]
    public void NextDeliverableInstant_IsANoOpOutsideQuietHours()
    {
        var preference = Preference(p =>
        {
            p.QuietHoursStart = 22;
            p.QuietHoursEnd = 8;
        });

        var midday = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(midday, preference.NextDeliverableInstant(midday));
    }
}

/// <summary>
/// The catalogue is the server's copy of text the frontend also holds. These tests are what
/// keep the two honest.
/// </summary>
public class NotificationCatalogTests
{
    [Fact]
    public void EveryKnownKey_HasTextInBothLanguages()
    {
        // Without this, adding a key and forgetting the Bosnian side produces an email in
        // the wrong language for the default locale — and nothing anywhere reports it.
        var missing = NotificationKeys.Categories.Keys
            .Where(key => !NotificationCatalog.HasKey(key))
            .ToList();

        Assert.True(missing.Count == 0,
            $"Keys with no catalogue entry in both languages: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryCatalogueKey_HasACategory()
    {
        // A key with no category falls back to System, which cannot be muted. Better to
        // find that here than to discover a category of email nobody can switch off.
        var uncategorised = NotificationCatalog.KnownKeys
            .Where(key => !NotificationKeys.Categories.ContainsKey(key))
            .ToList();

        Assert.True(uncategorised.Count == 0,
            $"Catalogue keys with no category: {string.Join(", ", uncategorised)}");
    }

    [Fact]
    public void Parameters_AreSubstituted()
    {
        var body = NotificationCatalog.Body(
            NotificationKeys.KudosReceived,
            new Dictionary<string, string> { ["fromName"] = "Amina", ["categoryLabel"] = "being helpful" },
            "en");

        Assert.Contains("Amina", body);
        Assert.DoesNotContain("{fromName}", body);
    }

    [Fact]
    public void MissingParameter_LeavesThePlaceholderVisible()
    {
        // Deliberate: a visible "{monthLabel}" in a test inbox is a bug report. Blanking it
        // produces a grammatical sentence with a hole in it that nobody notices.
        var body = NotificationCatalog.Body(
            NotificationKeys.JournalDue,
            new Dictionary<string, string> { ["daysLeft"] = "2" },
            "en");

        Assert.Contains("{monthLabel}", body);
    }

    [Fact]
    public void UnknownLocale_FallsBackRatherThanReturningTheKey()
    {
        var body = NotificationCatalog.Body(
            NotificationKeys.Welcome, new Dictionary<string, string>(), "fr");

        Assert.NotEqual(NotificationKeys.Welcome, body);
        Assert.False(string.IsNullOrWhiteSpace(body));
    }

    [Fact]
    public void UnknownKey_ReturnsTheKey_RatherThanEmpty()
    {
        // An empty subject is an email that looks broken; the key at least says what it was.
        var subject = NotificationCatalog.Subject("does.not.exist", null, "en");

        Assert.Equal("does.not.exist", subject);
    }

    [Fact]
    public void KeysWithADefaultAction_PointAtRelativePaths()
    {
        // An absolute URL here would be rendered as a button in email and in the app. Every
        // one of these must stay inside the site.
        foreach (var (key, action) in NotificationKeys.DefaultActions)
        {
            Assert.True(action.StartsWith('/'), $"{key} action '{action}' is not a relative path.");
            Assert.False(action.StartsWith("//"), $"{key} action '{action}' is protocol-relative.");
        }
    }
}

/// <summary>
/// The submission window. This rule used to live only in the browser, computed in local
/// time; these tests pin the behaviour that replaced it.
/// </summary>
public class JournalWindowTests
{
    private static JournalWindow Window(string monthYear, DateTime opens, DateTime closes) =>
        new(monthYear, monthYear, opens, closes);

    [Fact]
    public void DaysRemaining_IsNullOnceClosed_NotNegative()
    {
        // A negative countdown would render as "due in -3 days", and the reminder sweep
        // would match a threshold it should never match.
        var window = Window("2026-06",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 9, 22, 0, 0, DateTimeKind.Utc));

        Assert.Null(window.DaysRemainingAt(new DateTime(2026, 7, 12, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void DaysRemaining_FloorsToWholeDays()
    {
        var window = Window("2026-06",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 9, 22, 0, 0, DateTimeKind.Utc));

        // Two days and twenty-two hours out is "2", not "3" — the reminder thresholds are
        // whole days and rounding up would fire T-2 a day early.
        var now = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(2, window.DaysRemainingAt(now));
    }

    [Fact]
    public void OnTheClosingInstant_TheWindowIsStillOpen()
    {
        // Inclusive: "submit by the 9th" means the 9th counts.
        var closes = new DateTime(2026, 7, 9, 21, 59, 59, DateTimeKind.Utc);
        var window = Window("2026-06", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), closes);

        Assert.True(window.IsOpenAt(closes));
        Assert.False(window.IsOpenAt(closes.AddSeconds(1)));
    }

    [Fact]
    public void BeforeTheWindowOpens_ItIsNotOpen()
    {
        var window = Window("2026-06",
            new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 7, 9, 22, 0, 0, DateTimeKind.Utc));

        Assert.False(window.IsOpenAt(new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData("2026-06", true)]
    [InlineData("2026-6", false)]
    [InlineData("June 2026", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MonthParsing_AcceptsOnlyTheCanonicalFormat(string? input, bool expected)
    {
        Assert.Equal(expected, JournalWindowService.TryParseMonth(input, out _));
    }
}

/// <summary>
/// The VAPID key pair the push service validates every request against.
/// </summary>
public class WebPushKeyTests
{
    [Fact]
    public void GeneratedKeys_HaveTheLengthsThePushServicesRequire()
    {
        // The reason this test exists: .NET strips leading zero bytes when exporting EC
        // parameters, so roughly one key in 256 comes out a byte short. The push service
        // rejects it as malformed — intermittently, and only in production.
        for (var i = 0; i < 50; i++)
        {
            var (publicKey, privateKey) = WebPushSender.GenerateKeyPair();

            Assert.Equal(65, Decode(publicKey).Length);   // 0x04 ‖ X(32) ‖ Y(32)
            Assert.Equal(32, Decode(privateKey).Length);  // D
            Assert.Equal(0x04, Decode(publicKey)[0]);
        }
    }

    [Fact]
    public void GeneratedKeys_AreBase64UrlWithNoPadding()
    {
        var (publicKey, privateKey) = WebPushSender.GenerateKeyPair();

        foreach (var key in new[] { publicKey, privateKey })
        {
            Assert.DoesNotContain('+', key);
            Assert.DoesNotContain('/', key);
            Assert.DoesNotContain('=', key);
        }
    }

    [Fact]
    public void EachCallProducesADifferentPair()
    {
        var (firstPublic, _) = WebPushSender.GenerateKeyPair();
        var (secondPublic, _) = WebPushSender.GenerateKeyPair();

        Assert.NotEqual(firstPublic, secondPublic);
    }

    private static byte[] Decode(string base64Url)
    {
        var padded = base64Url.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}
