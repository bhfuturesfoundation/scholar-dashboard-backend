using System.Text.Json;
using Auth.Models.Constants;
using Auth.Models.Data;
using Auth.Models.DTOs.Notifications;
using Auth.Models.Entities.Notifications;
using Auth.Models.Enums.Notifications;
using Auth.Services.Interfaces.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Notifications
{
    /// <inheritdoc cref="INotificationService"/>
    public class NotificationService : INotificationService
    {
        /// <summary>
        /// How long a notification stays open to collapsing. Six hours means a busy
        /// afternoon of kudos reads as one line, while something that arrives the next
        /// morning is correctly a new event rather than a bumped old one.
        /// </summary>
        private static readonly TimeSpan CollapseWindow = TimeSpan.FromHours(6);

        /// <summary>
        /// Hard ceiling on what a single list request returns. The old client kept an
        /// unbounded array in localStorage and re-serialised the whole thing on every
        /// change; there is no reason for a bell menu to ever hold more than this.
        /// </summary>
        private const int MaxPageSize = 100;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _context;
        private readonly INotificationRealtime _realtime;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            ApplicationDbContext context,
            INotificationRealtime realtime,
            ILogger<NotificationService> logger)
        {
            _context = context;
            _realtime = realtime;
            _logger = logger;
        }

        // ── Creation ──────────────────────────────────────────────────────────

        public async Task<Notification?> CreateAsync(
            CreateNotificationRequest request, CancellationToken cancellationToken = default)
        {
            var created = await BuildAsync(request, DateTime.UtcNow, cancellationToken);

            // Saved unconditionally, including when BuildAsync returned null. A null result
            // means "no new row", but the collapse path gets there by *mutating* an existing
            // tracked row — returning early would silently discard the incremented count.
            // EF no-ops when the change tracker is clean, so the dedupe path costs nothing.
            await _context.SaveChangesAsync(cancellationToken);

            if (created is null) return null;

            await PublishAsync(created, cancellationToken);
            return created;
        }

        public async Task<int> CreateManyAsync(
            IReadOnlyCollection<CreateNotificationRequest> requests,
            CancellationToken cancellationToken = default)
        {
            if (requests.Count == 0) return 0;

            var now = DateTime.UtcNow;
            var created = new List<Notification>(requests.Count);

            foreach (var request in requests)
            {
                var notification = await BuildAsync(request, now, cancellationToken);
                if (notification is not null) created.Add(notification);
            }

            // Unconditional for the same reason as CreateAsync: a batch where every request
            // collapsed produces no new rows but still has pending mutations.
            await _context.SaveChangesAsync(cancellationToken);

            if (created.Count == 0) return 0;

            // Realtime is best-effort and per-user, so one dead connection cannot stop the
            // rest of a broadcast from reaching people who are online.
            foreach (var notification in created)
            {
                await PublishAsync(notification, cancellationToken);
            }

            return created.Count;
        }

        /// <summary>
        /// Applies dedupe, collapsing and preferences, and adds the row to the change
        /// tracker without saving. Returns null when the notification was suppressed.
        /// </summary>
        private async Task<Notification?> BuildAsync(
            CreateNotificationRequest request, DateTime now, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.MessageKey))
            {
                _logger.LogWarning("Ignoring a notification with no user or no message key.");
                return null;
            }

            var category = NotificationKeys.CategoryFor(request.MessageKey);

            // ── Dedupe ────────────────────────────────────────────────────────
            // Replaces the old localStorage "pushOnce" flags, which lived on whichever
            // device happened to be open at the time and leaked one key per month per
            // threshold that nothing ever cleaned up.
            if (!string.IsNullOrWhiteSpace(request.DedupeKey))
            {
                var exists = await _context.Notifications.AnyAsync(
                    n => n.UserId == request.UserId && n.DedupeKey == request.DedupeKey,
                    cancellationToken);

                if (exists) return null;
            }

            // ── Collapse ──────────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(request.CollapseKey))
            {
                var cutoff = now - CollapseWindow;

                var open = await _context.Notifications
                    .Where(n => n.UserId == request.UserId
                             && n.CollapseKey == request.CollapseKey
                             && n.ReadAt == null
                             && n.DismissedAt == null
                             && n.CreatedAt >= cutoff)
                    .OrderByDescending(n => n.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (open is not null)
                {
                    open.CollapseCount += 1;
                    open.CreatedAt = now;

                    // Swap to the plural wording once there is more than one. The singular
                    // names a person, which stops being true the moment it collapses.
                    open.MessageKey = PluralKeyFor(open.MessageKey);
                    open.ParamsJson = Serialise(new Dictionary<string, string>
                    {
                        ["count"] = open.CollapseCount.ToString()
                    });

                    // Deliberately does NOT reset EmailSentAt: someone who already had an
                    // email about the first one should not get a second for the collapse.
                    return null;
                }
            }

            // ── Preferences ───────────────────────────────────────────────────
            var preference = await GetPreferenceAsync(request.UserId, cancellationToken);

            var wantsEmail = request.WantsEmail && preference.Allows(category, NotificationChannel.Email);
            var wantsPush = request.WantsPush && preference.Allows(category, NotificationChannel.Push);

            // ── Quiet hours ───────────────────────────────────────────────────
            // Only ever defers the outbound channels. The in-app entry appears immediately,
            // because holding that back would mean someone who opens the app at 07:00 sees
            // nothing and is told about it an hour later.
            DateTime? deferredUntil = null;
            if ((wantsEmail || wantsPush) && preference.IsQuietAt(now))
            {
                deferredUntil = preference.NextDeliverableInstant(now);
            }

            var notification = new Notification
            {
                UserId = request.UserId,
                MessageKey = request.MessageKey,
                ParamsJson = request.Params.Count > 0 ? Serialise(request.Params) : null,
                Category = category,
                ActionUrl = request.ActionUrl ?? NotificationKeys.ActionFor(request.MessageKey),
                CreatedAt = now,
                DedupeKey = string.IsNullOrWhiteSpace(request.DedupeKey) ? null : request.DedupeKey,
                CollapseKey = string.IsNullOrWhiteSpace(request.CollapseKey) ? null : request.CollapseKey,
                CollapseCount = 1,
                WantsEmail = wantsEmail,
                WantsPush = wantsPush,
                DeferredUntil = deferredUntil,
                AnnouncementId = request.AnnouncementId
            };

            _context.Notifications.Add(notification);
            return notification;
        }

        /// <summary>
        /// The "several of these" variant of a key. Only kudos and achievements collapse
        /// today; anything else keeps its own key and simply counts up.
        /// </summary>
        private static string PluralKeyFor(string key) => key switch
        {
            NotificationKeys.KudosReceived or NotificationKeys.KudosReceivedMany
                => NotificationKeys.KudosReceivedMany,
            NotificationKeys.AchievementEarned or NotificationKeys.AchievementEarnedMany
                => NotificationKeys.AchievementEarnedMany,
            _ => key
        };

        private async Task PublishAsync(Notification notification, CancellationToken cancellationToken)
        {
            try
            {
                await _realtime.NotifyAsync(notification.UserId, ToDto(notification), cancellationToken);
            }
            catch (Exception ex)
            {
                // Cosmetic. The row is committed; the client picks it up on its next load.
                _logger.LogDebug(ex, "Realtime delivery failed for notification {Id}.", notification.Id);
            }
        }

        // ── Reading ───────────────────────────────────────────────────────────

        public async Task<NotificationListDto> GetForUserAsync(
            string userId, int limit = 50, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, MaxPageSize);

            var visible = _context.Notifications
                .AsNoTracking()
                .Where(n => n.UserId == userId && n.DismissedAt == null);

            var items = await visible
                .OrderByDescending(n => n.CreatedAt)
                .Take(limit)
                .ToListAsync(cancellationToken);

            // Two aggregates in one round trip rather than two queries — this endpoint is
            // polled, so it is the most frequently executed read in the app.
            var counts = await visible
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Unread = g.Count(n => n.ReadAt == null)
                })
                .FirstOrDefaultAsync(cancellationToken);

            return new NotificationListDto
            {
                Items = items.Select(ToDto).ToList(),
                UnreadCount = counts?.Unread ?? 0,
                TotalCount = counts?.Total ?? 0
            };
        }

        public async Task<int> MarkReadAsync(
            string userId, IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
        {
            if (ids.Count == 0) return 0;

            var now = DateTime.UtcNow;

            // Scoped to the caller's own rows. An endpoint that trusted the id alone would
            // let anyone mark somebody else's notifications read.
            var affected = await _context.Notifications
                .Where(n => n.UserId == userId && ids.Contains(n.Id) && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);

            if (affected > 0) await PublishUnreadCountAsync(userId, cancellationToken);
            return affected;
        }

        public async Task<int> MarkAllReadAsync(string userId, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            var affected = await _context.Notifications
                .Where(n => n.UserId == userId && n.ReadAt == null && n.DismissedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);

            if (affected > 0) await PublishUnreadCountAsync(userId, cancellationToken);
            return affected;
        }

        public async Task<bool> DismissAsync(string userId, int id, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            var affected = await _context.Notifications
                .Where(n => n.UserId == userId && n.Id == id && n.DismissedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.DismissedAt, now)
                    .SetProperty(n => n.ReadAt, n => n.ReadAt ?? now),
                    cancellationToken);

            if (affected > 0) await PublishUnreadCountAsync(userId, cancellationToken);
            return affected > 0;
        }

        public async Task<int> DismissAllAsync(string userId, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            var affected = await _context.Notifications
                .Where(n => n.UserId == userId && n.DismissedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.DismissedAt, now)
                    .SetProperty(n => n.ReadAt, n => n.ReadAt ?? now),
                    cancellationToken);

            if (affected > 0) await PublishUnreadCountAsync(userId, cancellationToken);
            return affected;
        }

        private async Task PublishUnreadCountAsync(string userId, CancellationToken cancellationToken)
        {
            try
            {
                var unread = await _context.Notifications.CountAsync(
                    n => n.UserId == userId && n.ReadAt == null && n.DismissedAt == null,
                    cancellationToken);

                await _realtime.NotifyUnreadCountAsync(userId, unread, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not publish unread count for {User}.", userId);
            }
        }

        // ── Preferences ───────────────────────────────────────────────────────

        public async Task<NotificationPreference> GetPreferenceAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            var existing = await _context.NotificationPreferences
                .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

            if (existing is not null) return existing;

            // Materialise defaults on first read so the settings screen and the send path
            // can never disagree about what "not configured" means.
            var created = new NotificationPreference { UserId = userId };
            _context.NotificationPreferences.Add(created);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Two concurrent requests can both miss and both insert; the unique index on
                // UserId turns the loser into an exception. Re-read rather than fail — the
                // row the winner wrote is exactly what this caller wanted.
                _context.Entry(created).State = EntityState.Detached;

                var raced = await _context.NotificationPreferences
                    .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

                if (raced is not null) return raced;
                throw;
            }

            return created;
        }

        public async Task<NotificationPreference> UpdatePreferenceAsync(
            string userId, NotificationPreferenceDto update, CancellationToken cancellationToken = default)
        {
            var preference = await GetPreferenceAsync(userId, cancellationToken);

            preference.EmailJournal = update.EmailJournal;
            preference.EmailKudos = update.EmailKudos;
            preference.EmailAchievements = update.EmailAchievements;
            preference.EmailAnnouncements = update.EmailAnnouncements;
            preference.EmailMentorship = update.EmailMentorship;
            preference.EmailWeeklyDigest = update.EmailWeeklyDigest;

            preference.PushJournal = update.PushJournal;
            preference.PushKudos = update.PushKudos;
            preference.PushAchievements = update.PushAchievements;
            preference.PushAnnouncements = update.PushAnnouncements;
            preference.PushMentorship = update.PushMentorship;
            preference.PushMinigames = update.PushMinigames;

            preference.QuietHoursEnabled = update.QuietHoursEnabled;
            preference.QuietHoursStart = Math.Clamp(update.QuietHoursStart, 0, 23);
            preference.QuietHoursEnd = Math.Clamp(update.QuietHoursEnd, 0, 23);

            // Validate the zone here rather than trusting the client. An unknown id would
            // otherwise sit in the row and make every quiet-hours check fall back to UTC,
            // silently ignoring the preference the scholar thought they had set.
            if (!string.IsNullOrWhiteSpace(update.TimeZoneId) && IsKnownTimeZone(update.TimeZoneId))
            {
                preference.TimeZoneId = update.TimeZoneId;
            }

            // Only locales the server can actually write email in. An unknown value would
            // silently fall through to Bosnian at send time, which looks like the preference
            // was ignored rather than rejected.
            if (!string.IsNullOrWhiteSpace(update.PreferredLocale)
                && (update.PreferredLocale is "bs" or "en"))
            {
                preference.PreferredLocale = update.PreferredLocale;
            }

            preference.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            return preference;
        }

        private static bool IsKnownTimeZone(string id)
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(id);
                return true;
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return false;
            }
        }

        // ── Mapping ───────────────────────────────────────────────────────────

        public static NotificationDto ToDto(Notification notification)
        {
            var parameters = Deserialise(notification.ParamsJson);

            return new NotificationDto
            {
                Id = notification.Id,
                MessageKey = notification.MessageKey,
                Params = parameters,
                Category = notification.Category,
                ActionUrl = notification.ActionUrl,
                CreatedAt = notification.CreatedAt,
                Read = notification.ReadAt is not null,
                CollapseCount = notification.CollapseCount,
                FallbackText = NotificationCatalog.FallbackText(notification.MessageKey, parameters)
            };
        }

        private static string Serialise(Dictionary<string, string> parameters) =>
            JsonSerializer.Serialize(parameters, JsonOptions);

        public static Dictionary<string, string> Deserialise(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                       ?? new Dictionary<string, string>();
            }
            catch (JsonException)
            {
                // Malformed JSON in one row must not break the whole bell menu.
                return new Dictionary<string, string>();
            }
        }
    }
}
