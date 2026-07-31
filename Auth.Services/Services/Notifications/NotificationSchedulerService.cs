using Auth.Models.Constants;
using Auth.Models.Data;
using Auth.Models.DTOs.Email;
using Auth.Models.Entities.Notifications;
using Auth.Models.Enums.Scholars;
using Auth.Services.Interfaces.Email;
using Auth.Services.Interfaces.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Notifications
{
    /// <summary>
    /// The part of the notification system that reaches people who are not looking at the app.
    ///
    /// This exists because of a specific failure in what came before it: journal reminders
    /// were a React effect that ran when the dashboard mounted. The scholar who most needs
    /// a deadline reminder is precisely the one who has not opened the dashboard since last
    /// month, so the reminder reached everyone except its audience.
    ///
    /// Three jobs, all idempotent, all safe to run on several instances at once:
    ///
    /// 1. <b>Outbox drain</b> — sends the email and push that notification rows have asked
    ///    for. Rows are the queue, so nothing is lost if a mail provider is down.
    /// 2. <b>Journal reminders</b> — creates deadline notifications at fixed days before the
    ///    window closes, and a notice once it has.
    /// 3. <b>Weekly digest</b> — one roll-up email for people who would rather not have one
    ///    message per event.
    ///
    /// Idempotence comes from dedupe keys and persisted timestamps rather than from an
    /// in-memory schedule, for the same reason <c>ScheduledBackupService</c> works that way:
    /// this app deploys often, and a timer that resets on deploy never fires.
    /// </summary>
    public class NotificationSchedulerService : BackgroundService
    {
        /// <summary>
        /// How often the outbox is drained. A minute is the worst-case delay between someone
        /// receiving kudos and the email going out, which is well inside what anyone notices.
        /// </summary>
        private static readonly TimeSpan OutboxInterval = TimeSpan.FromMinutes(1);

        /// <summary>
        /// How often reminders and digests are evaluated. Hourly is far more often than
        /// either fires, but it is two indexed queries, and it means an instance that was
        /// down at the scheduled hour catches up within the hour.
        /// </summary>
        private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

        /// <summary>How many outbound messages one drain will send. Keeps a backlog from stalling the loop.</summary>
        private const int OutboxBatchSize = 50;

        /// <summary>
        /// Spacing between sends, so a backlog does not arrive at the mail provider as a
        /// burst that looks like spam. Mirrors what MailingSchedulerService does.
        /// </summary>
        private static readonly TimeSpan SendDelay = TimeSpan.FromMilliseconds(250);

        /// <summary>Consecutive push failures before a subscription is abandoned.</summary>
        private const int MaxPushFailures = 5;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NotificationSchedulerService> _logger;

        private DateTime _lastSweepUtc = DateTime.MinValue;

        public NotificationSchedulerService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<NotificationSchedulerService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        private bool Enabled =>
            !string.Equals(_configuration["NOTIFICATIONS_SCHEDULER_ENABLED"]?.Trim(), "false",
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Days before the deadline a reminder is sent. Default 5, 2 and 0 — far enough out
        /// to act on, close enough to matter, and one on the day itself.
        /// </summary>
        private int[] ReminderDays
        {
            get
            {
                var raw = _configuration["JOURNAL_REMINDER_DAYS"];
                if (string.IsNullOrWhiteSpace(raw)) return new[] { 5, 2, 0 };

                var days = raw
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(d => int.TryParse(d, out var value) ? value : -1)
                    .Where(d => d is >= 0 and <= 31)
                    .Distinct()
                    .OrderByDescending(d => d)
                    .ToArray();

                return days.Length > 0 ? days : new[] { 5, 2, 0 };
            }
        }

        /// <summary>Day of the week the weekly digest goes out. Default Monday.</summary>
        private DayOfWeek DigestDay =>
            Enum.TryParse<DayOfWeek>(_configuration["DIGEST_DAY"], ignoreCase: true, out var day)
                ? day
                : DayOfWeek.Monday;

        /// <summary>Hour (UTC) the digest goes out. Default 07:00.</summary>
        private int DigestHourUtc =>
            int.TryParse(_configuration["DIGEST_HOUR_UTC"], out var hour) && hour is >= 0 and <= 23
                ? hour
                : 7;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled)
            {
                _logger.LogInformation("Notification scheduler is disabled (NOTIFICATIONS_SCHEDULER_ENABLED=false).");
                return;
            }

            _logger.LogInformation(
                "Notification scheduler active. Reminders at T-{Days} days; digest on {Day} at {Hour:00}:00 UTC.",
                string.Join("/", ReminderDays), DigestDay, DigestHourUtc);

            // Let migrations and seeding finish before touching the database.
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await DrainOutboxAsync(stoppingToken);

                    if (DateTime.UtcNow - _lastSweepUtc >= SweepInterval)
                    {
                        _lastSweepUtc = DateTime.UtcNow;
                        await SweepJournalRemindersAsync(stoppingToken);
                        await SweepWeeklyDigestAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A BackgroundService that throws is torn down permanently and silently.
                    _logger.LogError(ex, "Notification scheduler tick failed. Continuing.");
                }

                try { await Task.Delay(OutboxInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        // ── 1. Outbox drain ───────────────────────────────────────────────────

        private async Task DrainOutboxAsync(CancellationToken cancellationToken)
        {
            await DrainEmailAsync(cancellationToken);
            await DrainPushAsync(cancellationToken);
        }

        private async Task DrainEmailAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IEmailDispatcher>();
            var renderer = scope.ServiceProvider.GetRequiredService<IEmailTemplateRenderer>();

            var now = DateTime.UtcNow;

            var pending = await context.Notifications
                .Include(n => n.User)
                .Where(n => n.WantsEmail
                         && n.EmailSentAt == null
                         && (n.DeferredUntil == null || n.DeferredUntil <= now))
                .OrderBy(n => n.CreatedAt)
                .Take(OutboxBatchSize)
                .ToListAsync(cancellationToken);

            if (pending.Count == 0) return;

            var userIds = pending.Select(n => n.UserId).Distinct().ToList();
            var locales = await context.NotificationPreferences
                .AsNoTracking()
                .Where(p => userIds.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId, p => p.PreferredLocale, cancellationToken);

            foreach (var notification in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var email = notification.User?.Email;
                if (string.IsNullOrWhiteSpace(email))
                {
                    // Nothing to send to, and it will never become sendable. Mark it done so
                    // the drain does not pick the same row up forever.
                    notification.EmailSentAt = now;
                    continue;
                }

                var locale = locales.GetValueOrDefault(notification.UserId, NotificationCatalog.DefaultLocale);
                var parameters = NotificationService.Deserialise(notification.ParamsJson);

                var subject = NotificationCatalog.Subject(notification.MessageKey, parameters, locale);
                var body = NotificationCatalog.Body(notification.MessageKey, parameters, locale);

                // Rendered through the same branded layout as every other email, and with no
                // variables — the text is already substituted, and passing it through the
                // {{variable}} renderer again would let a scholar's own words be treated as
                // a placeholder.
                var rendered = renderer.Render(subject, body, new Dictionary<string, string?>());

                var result = await dispatcher.SendAsync(new OutboundEmail
                {
                    ToEmail = email,
                    ToName = $"{notification.User?.FirstName} {notification.User?.LastName}".Trim(),
                    Subject = rendered.Subject,
                    HtmlBody = rendered.HtmlBody,
                    TextBody = rendered.TextBody,
                    Tag = $"notification:{notification.Category}"
                }, cancellationToken: cancellationToken);

                // Marked sent either way. A hard failure retried every minute forever would
                // be worse than one lost reminder — the notification is still in the app.
                notification.EmailSentAt = now;

                if (result.WasSuppressed)
                {
                    // Expected, not a fault: the address is on the suppression list or the
                    // account is inactive. Debug so a deactivated cohort does not fill the
                    // log with warnings every time a reminder sweep runs.
                    _logger.LogDebug(
                        "Notification {Id} email to {User} was suppressed: {Error}",
                        notification.Id, notification.UserId, result.Error);
                }
                else if (!result.Success)
                {
                    _logger.LogWarning(
                        "Notification {Id} email to {User} was not delivered: {Error}",
                        notification.Id, notification.UserId, result.Error);
                }

                try { await Task.Delay(SendDelay, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        private async Task DrainPushAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var push = scope.ServiceProvider.GetRequiredService<IPushSender>();

            if (!push.IsConfigured) return;

            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.UtcNow;

            var pending = await context.Notifications
                .Where(n => n.WantsPush
                         && n.PushSentAt == null
                         && (n.DeferredUntil == null || n.DeferredUntil <= now))
                .OrderBy(n => n.CreatedAt)
                .Take(OutboxBatchSize)
                .ToListAsync(cancellationToken);

            if (pending.Count == 0) return;

            var userIds = pending.Select(n => n.UserId).Distinct().ToList();

            var subscriptions = await context.PushSubscriptions
                .Where(s => userIds.Contains(s.UserId))
                .ToListAsync(cancellationToken);

            var byUser = subscriptions
                .GroupBy(s => s.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var locales = await context.NotificationPreferences
                .AsNoTracking()
                .Where(p => userIds.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId, p => p.PreferredLocale, cancellationToken);

            var dead = new List<Auth.Models.Entities.Notifications.PushSubscription>();

            foreach (var notification in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();

                notification.PushSentAt = now;

                if (!byUser.TryGetValue(notification.UserId, out var devices) || devices.Count == 0)
                {
                    continue;
                }

                var locale = locales.GetValueOrDefault(notification.UserId, NotificationCatalog.DefaultLocale);
                var parameters = NotificationService.Deserialise(notification.ParamsJson);

                var payload = new PushPayload
                {
                    Title = NotificationCatalog.Subject(notification.MessageKey, parameters, locale),
                    Body = NotificationCatalog.Body(notification.MessageKey, parameters, locale),
                    Url = notification.ActionUrl,

                    // Same dedupe key means the device replaces rather than stacks, so a
                    // reminder delivered to two devices does not read as two events.
                    Tag = notification.DedupeKey ?? $"n-{notification.Id}",
                    NotificationId = notification.Id
                };

                foreach (var device in devices)
                {
                    var result = await push.SendAsync(
                        new PushTarget(device.Endpoint, device.P256dh, device.Auth),
                        payload,
                        cancellationToken);

                    if (result.Success)
                    {
                        device.LastSuccessAt = now;
                        device.FailureCount = 0;
                    }
                    else if (result.SubscriptionExpired)
                    {
                        // The browser was cleared or the app uninstalled. Never coming back.
                        dead.Add(device);
                    }
                    else if (++device.FailureCount >= MaxPushFailures)
                    {
                        dead.Add(device);
                    }
                }
            }

            if (dead.Count > 0)
            {
                context.PushSubscriptions.RemoveRange(dead);
                _logger.LogInformation("Removed {Count} dead push subscription(s).", dead.Count);
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        // ── 2. Journal reminders ──────────────────────────────────────────────

        private async Task SweepJournalRemindersAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var windows = scope.ServiceProvider.GetRequiredService<IJournalWindowService>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var now = DateTime.UtcNow;
            var window = windows.GetCurrentWindow(now);
            var daysRemaining = window.DaysRemainingAt(now);

            // Which reminder, if any, today is. Null outside the window entirely.
            string? threshold = null;
            string messageKey;

            if (window.IsOpenAt(now) && daysRemaining is { } days)
            {
                var match = ReminderDays.FirstOrDefault(d => d == days, -1);
                if (match < 0) return;

                threshold = $"t-{match}";
                messageKey = match == 0 ? NotificationKeys.JournalDueToday : NotificationKeys.JournalDue;
            }
            else if (now > window.ClosesAtUtc && now - window.ClosesAtUtc < TimeSpan.FromHours(36))
            {
                // A single "the window has closed" notice, within a day and a half of the
                // close so it is still news. Not a reminder — there is nothing left to do —
                // but a scholar who missed it should learn that from the app rather than
                // from their program manager.
                threshold = "closed";
                messageKey = NotificationKeys.JournalWindowClosed;
            }
            else
            {
                return;
            }

            var outstanding = await FindScholarsWithoutSubmissionAsync(context, window.MonthYear, cancellationToken);
            if (outstanding.Count == 0) return;

            var deadlineLabel = FormatDeadline(window.ClosesAtUtc);

            var requests = outstanding.Select(userId => new CreateNotificationRequest
            {
                UserId = userId,
                MessageKey = messageKey,
                Params = new Dictionary<string, string>
                {
                    ["monthLabel"] = window.MonthLabel,
                    ["daysLeft"] = (daysRemaining ?? 0).ToString(),
                    ["deadline"] = deadlineLabel
                },

                // The reason this can run hourly without spamming: one row per scholar per
                // month per threshold, enforced by a unique index rather than by hoping the
                // schedule never overlaps.
                DedupeKey = $"journal:{window.MonthYear}:{threshold}",
                WantsEmail = true,
                WantsPush = true
            }).ToList();

            var created = await notifications.CreateManyAsync(requests, cancellationToken);

            if (created > 0)
            {
                _logger.LogInformation(
                    "Journal reminder ({Threshold}) for {Month} created for {Count} scholar(s).",
                    threshold, window.MonthYear, created);
            }
        }

        /// <summary>
        /// Active scholars who have not submitted for the month.
        ///
        /// Alumni and withdrawn scholars are excluded: the programme is over for them and a
        /// deadline reminder would be both wrong and unkind. Inactive accounts are excluded
        /// too — email to them is suppressed at dispatch anyway, but there is no reason to
        /// create the notification in the first place.
        /// </summary>
        private static async Task<List<string>> FindScholarsWithoutSubmissionAsync(
            ApplicationDbContext context, string monthYear, CancellationToken cancellationToken)
        {
            var scholarRoleId = await context.Roles
                .Where(r => r.Name == AppRoles.User)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (scholarRoleId is null) return new List<string>();

            var scholarIds = context.UserRoles
                .Where(ur => ur.RoleId == scholarRoleId)
                .Select(ur => ur.UserId);

            var submitted = context.JournalSubmissions
                .Where(s => s.MonthYear == monthYear && s.Submitted)
                .Select(s => s.ScholarId);

            return await context.Users
                .AsNoTracking()
                .Where(u => u.IsActive
                         && scholarIds.Contains(u.Id)
                         && u.ScholarStatus != ScholarStatus.Alumni
                         && u.ScholarStatus != ScholarStatus.Withdrawn
                         && !submitted.Contains(u.Id))
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);
        }

        // ── 3. Weekly digest ──────────────────────────────────────────────────

        private async Task SweepWeeklyDigestAsync(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            if (now.DayOfWeek != DigestDay || now.Hour < DigestHourUtc) return;

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IEmailDispatcher>();
            var renderer = scope.ServiceProvider.GetRequiredService<IEmailTemplateRenderer>();
            var windows = scope.ServiceProvider.GetRequiredService<IJournalWindowService>();

            // Anyone whose last digest was more than six days ago. Six rather than seven so
            // a run that slips by an hour week to week does not eventually skip one.
            var cutoff = now.AddDays(-6);

            var candidates = await context.NotificationPreferences
                .Include(p => p.User)
                .Where(p => p.EmailWeeklyDigest
                         && (p.LastDigestAt == null || p.LastDigestAt < cutoff)
                         && p.User.IsActive)
                .Take(200)
                .ToListAsync(cancellationToken);

            if (candidates.Count == 0) return;

            var window = windows.GetCurrentWindow(now);
            var weekAgo = now.AddDays(-7);
            var sent = 0;

            foreach (var preference in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var email = preference.User?.Email;
                if (string.IsNullOrWhiteSpace(email))
                {
                    preference.LastDigestAt = now;
                    continue;
                }

                var recent = await context.Notifications
                    .AsNoTracking()
                    .Where(n => n.UserId == preference.UserId
                             && n.CreatedAt >= weekAgo
                             && n.DismissedAt == null)
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(20)
                    .ToListAsync(cancellationToken);

                var submitted = await context.JournalSubmissions.AnyAsync(
                    s => s.ScholarId == preference.UserId
                      && s.MonthYear == window.MonthYear
                      && s.Submitted,
                    cancellationToken);

                var outstanding = window.IsOpenAt(now) && !submitted;

                // Nothing happened and nothing is due. Sending "you have no updates" weekly
                // is the fastest way to train someone to filter you into spam.
                if (recent.Count == 0 && !outstanding)
                {
                    preference.LastDigestAt = now;
                    continue;
                }

                var (subject, body) = BuildDigest(
                    preference, recent, outstanding, window, now);

                var rendered = renderer.Render(subject, body, new Dictionary<string, string?>());

                await dispatcher.SendAsync(new OutboundEmail
                {
                    ToEmail = email,
                    ToName = $"{preference.User?.FirstName} {preference.User?.LastName}".Trim(),
                    Subject = rendered.Subject,
                    HtmlBody = rendered.HtmlBody,
                    TextBody = rendered.TextBody,
                    Tag = "notification:digest"
                }, cancellationToken: cancellationToken);

                preference.LastDigestAt = now;
                sent++;

                try { await Task.Delay(SendDelay, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }

            await context.SaveChangesAsync(cancellationToken);

            if (sent > 0) _logger.LogInformation("Weekly digest sent to {Count} recipient(s).", sent);
        }

        private static (string Subject, string Body) BuildDigest(
            NotificationPreference preference,
            IReadOnlyList<Notification> recent,
            bool journalOutstanding,
            JournalWindow window,
            DateTime now)
        {
            var locale = preference.PreferredLocale;
            var bosnian = !string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase);

            var subject = bosnian ? "Vaš sedmični pregled" : "Your week on the Scholar Dashboard";

            var lines = new List<string>();

            if (journalOutstanding)
            {
                var days = window.DaysRemainingAt(now) ?? 0;
                lines.Add(bosnian
                    ? $"Dnevnik za {window.MonthLabel} još nije predan — preostalo dana: {days}."
                    : $"Your {window.MonthLabel} journal is still outstanding — {days} day(s) left.");
                lines.Add(string.Empty);
            }

            if (recent.Count > 0)
            {
                lines.Add(bosnian ? "Šta se dogodilo ove sedmice:" : "What happened this week:");
                lines.Add(string.Empty);

                foreach (var notification in recent)
                {
                    var parameters = NotificationService.Deserialise(notification.ParamsJson);
                    lines.Add("• " + NotificationCatalog.Body(notification.MessageKey, parameters, locale));
                }

                lines.Add(string.Empty);
            }

            lines.Add(bosnian
                ? "Ovaj pregled možete isključiti u postavkama obavještenja."
                : "You can turn this summary off in your notification settings.");

            return (subject, string.Join("\n", lines));
        }

        /// <summary>
        /// The deadline as a scholar would say it, in programme-local time. Formatted here
        /// rather than client-side because it goes into an email, where there is no browser
        /// to do it.
        /// </summary>
        private static string FormatDeadline(DateTime closesAtUtc)
        {
            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Sarajevo");
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                zone = TimeZoneInfo.Utc;
            }

            var local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(closesAtUtc, DateTimeKind.Utc), zone);

            return local.ToString("d MMMM, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
