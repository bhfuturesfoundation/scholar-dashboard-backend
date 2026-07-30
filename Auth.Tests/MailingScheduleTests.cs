using Auth.Models.Entities.Mailing;
using Auth.Models.Enums.Mailing;

namespace Auth.Tests;

/// <summary>
/// Tests for schedule cadence and the send-window logic.
///
/// These controls exist for deliverability, not convenience: mailing 400 firms in one burst
/// at 03:00 is the fastest route into a spam folder. The window arithmetic is easy to get
/// subtly wrong — particularly a window that wraps midnight — and a wrong answer either
/// stops a schedule firing forever or fires it at 4am.
/// </summary>
public class MailingScheduleTests
{
    private static MailingSchedule Schedule(int startHour, int endHour) => new()
    {
        Name = "Test",
        SendWindowStartHourUtc = startHour,
        SendWindowEndHourUtc = endHour
    };

    private static DateTime At(int hour) => new(2026, 7, 30, hour, 0, 0, DateTimeKind.Utc);

    // ── Normal window ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(7, true)]
    [InlineData(12, true)]
    [InlineData(16, true)]
    public void InsideBusinessHoursWindow_IsAllowed(int hour, bool expected)
    {
        var schedule = Schedule(7, 17);
        Assert.Equal(expected, schedule.IsWithinSendWindow(At(hour)));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(17)]
    [InlineData(23)]
    public void OutsideBusinessHoursWindow_IsBlocked(int hour)
    {
        var schedule = Schedule(7, 17);
        Assert.False(schedule.IsWithinSendWindow(At(hour)));
    }

    [Fact]
    public void WindowEndIsExclusive()
    {
        // 17:00 with an end of 17 must be blocked, or "until 5pm" silently means "until 6pm".
        var schedule = Schedule(7, 17);

        Assert.True(schedule.IsWithinSendWindow(At(16)));
        Assert.False(schedule.IsWithinSendWindow(At(17)));
    }

    // ── Wrapping window ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(22, true)]
    [InlineData(23, true)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(12, false)]
    public void WindowWrappingMidnight_IsHandled(int hour, bool expected)
    {
        // 22:00 → 04:00. Naive start<=hour<end arithmetic gets every one of these wrong.
        var schedule = Schedule(22, 4);
        Assert.Equal(expected, schedule.IsWithinSendWindow(At(hour)));
    }

    [Fact]
    public void EqualStartAndEnd_MeansNoRestriction()
    {
        // Otherwise the window would be a single instant and the schedule would never fire.
        var schedule = Schedule(0, 0);

        foreach (var hour in Enumerable.Range(0, 24))
            Assert.True(schedule.IsWithinSendWindow(At(hour)), $"Hour {hour} was blocked");
    }

    // ── Cadence ───────────────────────────────────────────────────────────────

    [Fact]
    public void OnceCadence_HasNoNextRun()
    {
        var schedule = new MailingSchedule { Cadence = ScheduleCadence.Once };

        Assert.Null(schedule.ComputeNextRun(DateTime.UtcNow));
    }

    [Fact]
    public void FixedInterval_AddsTheInterval()
    {
        var schedule = new MailingSchedule { Cadence = ScheduleCadence.FixedInterval, IntervalMinutes = 90 };
        var from = At(10);

        Assert.Equal(from.AddMinutes(90), schedule.ComputeNextRun(from));
    }

    [Fact]
    public void FixedInterval_IsFloorClampedTo15Minutes()
    {
        // Below the scheduler's own poll cadence the interval is meaningless, and it stops
        // resembling human-paced outreach.
        var schedule = new MailingSchedule { Cadence = ScheduleCadence.FixedInterval, IntervalMinutes = 1 };
        var from = At(10);

        Assert.Equal(from.AddMinutes(15), schedule.ComputeNextRun(from));
    }

    [Theory]
    [InlineData(ScheduleCadence.Daily, 1)]
    [InlineData(ScheduleCadence.Weekly, 7)]
    public void CalendarCadences_AdvanceByDays(ScheduleCadence cadence, int expectedDays)
    {
        var schedule = new MailingSchedule { Cadence = cadence };
        var from = At(9);

        Assert.Equal(from.AddDays(expectedDays), schedule.ComputeNextRun(from));
    }

    [Fact]
    public void MonthlyCadence_AdvancesByCalendarMonth()
    {
        var schedule = new MailingSchedule { Cadence = ScheduleCadence.Monthly };
        var from = new DateTime(2026, 1, 31, 9, 0, 0, DateTimeKind.Utc);

        // AddMonths clamps 31 Jan to 28 Feb rather than overflowing into March.
        Assert.Equal(new DateTime(2026, 2, 28, 9, 0, 0, DateTimeKind.Utc), schedule.ComputeNextRun(from));
    }

    // ── Send cap ──────────────────────────────────────────────────────────────

    [Fact]
    public void NoCap_NeverReportsReached()
    {
        var schedule = new MailingSchedule { MaxTotalSends = null, TotalSent = 100_000 };

        Assert.False(schedule.HasReachedCap);
    }

    [Theory]
    [InlineData(10, 9, false)]
    [InlineData(10, 10, true)]
    [InlineData(10, 11, true)]
    public void Cap_IsReachedAtOrAboveTheLimit(int cap, int sent, bool expected)
    {
        var schedule = new MailingSchedule { MaxTotalSends = cap, TotalSent = sent };

        Assert.Equal(expected, schedule.HasReachedCap);
    }

    // ── Template variant resolution ───────────────────────────────────────────

    [Fact]
    public void PersonVariantUsedOnlyWhenEnabledAndFirmHasName()
    {
        var template = new MailingTemplate
        {
            SubjectFirmVariant = "Hello {{firmName}}",
            BodyFirmVariant = "Dear {{firmName}}",
            PersonVariantEnabled = true,
            SubjectPersonVariant = "Hello {{firstName}}",
            BodyPersonVariant = "Dear {{firstName}}"
        };

        Assert.Equal(TemplateVariant.Person, template.ResolveVariant(firmHasUsableContactName: true));
        Assert.Equal(TemplateVariant.Firm, template.ResolveVariant(firmHasUsableContactName: false));
    }

    [Fact]
    public void PersonVariantDisabled_AlwaysUsesFirmVariant()
    {
        var template = new MailingTemplate
        {
            SubjectFirmVariant = "Hello {{firmName}}",
            BodyFirmVariant = "Dear {{firmName}}",
            PersonVariantEnabled = false,
            SubjectPersonVariant = "Hello {{firstName}}",
            BodyPersonVariant = "Dear {{firstName}}"
        };

        Assert.Equal(TemplateVariant.Firm, template.ResolveVariant(firmHasUsableContactName: true));
    }

    [Fact]
    public void PersonVariantEnabledButEmpty_FallsBackToFirmVariant()
    {
        // Guards against sending an empty body because someone ticked the box and didn't
        // fill the fields in.
        var template = new MailingTemplate
        {
            SubjectFirmVariant = "Hello {{firmName}}",
            BodyFirmVariant = "Dear {{firmName}}",
            PersonVariantEnabled = true,
            SubjectPersonVariant = null,
            BodyPersonVariant = null
        };

        Assert.False(template.SupportsPersonVariant);
        Assert.Equal(TemplateVariant.Firm, template.ResolveVariant(firmHasUsableContactName: true));
        Assert.Equal("Dear {{firmName}}", template.ResolveBody(TemplateVariant.Person));
    }

    [Fact]
    public void FirmWithLowConfidenceName_DoesNotGetPersonVariant()
    {
        // "Dear A. Hodzic" from an initial-and-surname mailbox reads worse than addressing
        // the organisation.
        var firm = new Firm
        {
            Name = "Acme d.o.o.",
            ContactPersonName = "A. Hodzic",
            ContactNameConfidence = NameConfidence.Low
        };

        Assert.False(firm.HasUsableContactName);
    }
}
