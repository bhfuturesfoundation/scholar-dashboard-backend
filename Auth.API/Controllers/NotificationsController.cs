using System.Security.Claims;
using Auth.Models.Constants;
using Auth.Models.Data;
using Auth.Models.DTOs.Notifications;
using Auth.Models.Response;
using Auth.Services.Interfaces.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auth.API.Controllers
{
    /// <summary>
    /// The bell menu, delivery preferences, push subscriptions, the journal window, and
    /// staff announcements.
    ///
    /// Every read and write is scoped to the caller's own id taken from the token, never
    /// from a route parameter. The announcement endpoints are the only exception and they
    /// are role-gated — an endpoint that accepted a user id would let any signed-in account
    /// read somebody else's notifications, which include the content of staff messages and
    /// who recognised whom.
    /// </summary>
    [Route("api/notifications")]
    [ApiController]
    [Authorize]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notifications;
        private readonly IJournalWindowService _windows;
        private readonly IAnnouncementService _announcements;
        private readonly IPushSender _push;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            INotificationService notifications,
            IJournalWindowService windows,
            IAnnouncementService announcements,
            IPushSender push,
            ApplicationDbContext context,
            ILogger<NotificationsController> logger)
        {
            _notifications = notifications;
            _windows = windows;
            _announcements = announcements;
            _push = push;
            _context = context;
            _logger = logger;
        }

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

        private string UserName =>
            User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue(ClaimTypes.Email)
            ?? "Staff";

        // ── The bell menu ─────────────────────────────────────────────────────

        [HttpGet]
        public async Task<ActionResult<ApiResponse<NotificationListDto>>> GetMine(
            [FromQuery] int limit = 50, CancellationToken ct = default) =>
            Ok(ApiResponse<NotificationListDto>.SuccessResponse(
                await _notifications.GetForUserAsync(UserId, limit, ct), "Notifications retrieved"));

        [HttpPost("read")]
        public async Task<ActionResult<ApiResponse<int>>> MarkRead(
            [FromBody] MarkReadRequest request, CancellationToken ct)
        {
            var affected = request.Ids is { Count: > 0 }
                ? await _notifications.MarkReadAsync(UserId, request.Ids, ct)
                : await _notifications.MarkAllReadAsync(UserId, ct);

            return Ok(ApiResponse<int>.SuccessResponse(affected, "Marked read"));
        }

        [HttpPost("read-all")]
        public async Task<ActionResult<ApiResponse<int>>> MarkAllRead(CancellationToken ct) =>
            Ok(ApiResponse<int>.SuccessResponse(
                await _notifications.MarkAllReadAsync(UserId, ct), "Marked read"));

        [HttpDelete("{id:int}")]
        public async Task<ActionResult<ApiResponse<bool>>> Dismiss(int id, CancellationToken ct) =>
            Ok(ApiResponse<bool>.SuccessResponse(
                await _notifications.DismissAsync(UserId, id, ct), "Dismissed"));

        [HttpDelete]
        public async Task<ActionResult<ApiResponse<int>>> DismissAll(CancellationToken ct) =>
            Ok(ApiResponse<int>.SuccessResponse(
                await _notifications.DismissAllAsync(UserId, ct), "Cleared"));

        // ── Preferences ───────────────────────────────────────────────────────

        [HttpGet("preferences")]
        public async Task<ActionResult<ApiResponse<NotificationPreferenceDto>>> GetPreferences(CancellationToken ct)
        {
            var preference = await _notifications.GetPreferenceAsync(UserId, ct);
            return Ok(ApiResponse<NotificationPreferenceDto>.SuccessResponse(
                await ToDtoAsync(preference, ct), "Preferences retrieved"));
        }

        [HttpPut("preferences")]
        public async Task<ActionResult<ApiResponse<NotificationPreferenceDto>>> UpdatePreferences(
            [FromBody] NotificationPreferenceDto request, CancellationToken ct)
        {
            var preference = await _notifications.UpdatePreferenceAsync(UserId, request, ct);
            return Ok(ApiResponse<NotificationPreferenceDto>.SuccessResponse(
                await ToDtoAsync(preference, ct), "Preferences saved"));
        }

        /// <summary>
        /// Records the reader's language without touching the rest of the preference matrix.
        ///
        /// Its own endpoint because the language switcher fires on a click anywhere in the
        /// app, and a full preferences PUT from a screen that never loaded the preferences
        /// would write back defaults over whatever the scholar had actually chosen.
        /// </summary>
        [HttpPut("preferences/locale")]
        public async Task<ActionResult<ApiResponse<bool>>> SetLocale(
            [FromBody] SetLocaleRequest request, CancellationToken ct)
        {
            var current = await _notifications.GetPreferenceAsync(UserId, ct);

            var dto = ToPlainDto(current);
            dto.PreferredLocale = request.Locale;

            await _notifications.UpdatePreferenceAsync(UserId, dto, ct);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Locale saved"));
        }

        private NotificationPreferenceDto ToPlainDto(Auth.Models.Entities.Notifications.NotificationPreference p) => new()
        {
            EmailJournal = p.EmailJournal,
            EmailKudos = p.EmailKudos,
            EmailAchievements = p.EmailAchievements,
            EmailAnnouncements = p.EmailAnnouncements,
            EmailMentorship = p.EmailMentorship,
            EmailWeeklyDigest = p.EmailWeeklyDigest,
            PushJournal = p.PushJournal,
            PushKudos = p.PushKudos,
            PushAchievements = p.PushAchievements,
            PushAnnouncements = p.PushAnnouncements,
            PushMentorship = p.PushMentorship,
            PushMinigames = p.PushMinigames,
            QuietHoursEnabled = p.QuietHoursEnabled,
            QuietHoursStart = p.QuietHoursStart,
            QuietHoursEnd = p.QuietHoursEnd,
            TimeZoneId = p.TimeZoneId,
            PreferredLocale = p.PreferredLocale
        };

        private async Task<NotificationPreferenceDto> ToDtoAsync(
            Auth.Models.Entities.Notifications.NotificationPreference p, CancellationToken ct)
        {
            var dto = ToPlainDto(p);

            dto.PushAvailable = _push.IsConfigured;
            dto.PushPublicKey = _push.PublicKey;
            dto.PushDeviceCount = await _context.PushSubscriptions
                .CountAsync(s => s.UserId == p.UserId, ct);

            return dto;
        }

        // ── Push subscriptions ────────────────────────────────────────────────

        [HttpPost("push/subscribe")]
        public async Task<ActionResult<ApiResponse<bool>>> Subscribe(
            [FromBody] PushSubscriptionRequest request, CancellationToken ct)
        {
            if (!_push.IsConfigured)
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse(_push.ConfigurationHint));
            }

            if (string.IsNullOrWhiteSpace(request.Endpoint)
                || string.IsNullOrWhiteSpace(request.P256dh)
                || string.IsNullOrWhiteSpace(request.Auth))
            {
                return BadRequest(ApiResponse<bool>.ErrorResponse("Subscription is missing an endpoint or a key."));
            }

            var existing = await _context.PushSubscriptions
                .FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);

            if (existing is not null)
            {
                // Reassign rather than reject. The endpoint identifies a physical browser; if
                // somebody else signs in on it, the previous account's notifications must
                // stop arriving there.
                existing.UserId = UserId;
                existing.P256dh = request.P256dh;
                existing.Auth = request.Auth;
                existing.UserAgent = Truncate(request.UserAgent, 400);
                existing.FailureCount = 0;
            }
            else
            {
                _context.PushSubscriptions.Add(new Auth.Models.Entities.Notifications.PushSubscription
                {
                    UserId = UserId,
                    Endpoint = request.Endpoint,
                    P256dh = request.P256dh,
                    Auth = request.Auth,
                    UserAgent = Truncate(request.UserAgent, 400)
                });
            }

            await _context.SaveChangesAsync(ct);
            return Ok(ApiResponse<bool>.SuccessResponse(true, "Push enabled on this device"));
        }

        [HttpPost("push/unsubscribe")]
        public async Task<ActionResult<ApiResponse<bool>>> Unsubscribe(
            [FromBody] PushSubscriptionRequest request, CancellationToken ct)
        {
            // Scoped to the caller: an endpoint string alone would let anyone who learned it
            // silently switch off somebody else's push.
            var removed = await _context.PushSubscriptions
                .Where(s => s.UserId == UserId && s.Endpoint == request.Endpoint)
                .ExecuteDeleteAsync(ct);

            return Ok(ApiResponse<bool>.SuccessResponse(removed > 0, "Push disabled on this device"));
        }

        private static string? Truncate(string? value, int max) =>
            string.IsNullOrWhiteSpace(value) ? null
            : value.Length <= max ? value
            : value[..max];

        /// <summary>
        /// Generates a VAPID key pair for an admin to paste into the environment.
        ///
        /// Generating rather than documenting a CLI incantation, because the alternative is
        /// installing Node just to run <c>web-push generate-vapid-keys</c> once. Admin-only
        /// and deliberately never stored: this returns a fresh pair and forgets it, so the
        /// endpoint cannot be used to read the keys currently in use.
        /// </summary>
        [HttpPost("push/generate-keys")]
        [Authorize(Roles = AppRoles.Admin)]
        public ActionResult<ApiResponse<object>> GenerateVapidKeys()
        {
            var (publicKey, privateKey) = Auth.Services.Services.Notifications.WebPushSender.GenerateKeyPair();

            _logger.LogInformation("A VAPID key pair was generated by {User}.", UserId);

            return Ok(ApiResponse<object>.SuccessResponse(new
            {
                publicKey,
                privateKey,
                instructions =
                    "Set VAPID_PUBLIC_KEY and VAPID_PRIVATE_KEY in the environment and redeploy. " +
                    "Keep the private key secret. Rotating the pair invalidates every existing " +
                    "push subscription, because browsers bind to the public key they were given."
            }, "Key pair generated"));
        }

        // ── Journal window ────────────────────────────────────────────────────

        /// <summary>
        /// The submission window as UTC instants, replacing the copy the frontend used to
        /// compute from the browser's local clock.
        /// </summary>
        [HttpGet("journal-window")]
        public async Task<ActionResult<ApiResponse<JournalWindowDto>>> GetJournalWindow(CancellationToken ct) =>
            Ok(ApiResponse<JournalWindowDto>.SuccessResponse(
                await _windows.GetForScholarAsync(UserId, DateTime.UtcNow, ct), "Window retrieved"));

        // ── Announcements (staff) ─────────────────────────────────────────────

        [HttpPost("announcements/preview")]
        [Authorize(Roles = AppRoles.JournalOversight)]
        public async Task<ActionResult<ApiResponse<AudiencePreviewDto>>> PreviewAnnouncement(
            [FromBody] AnnouncementRequest request, CancellationToken ct) =>
            Ok(ApiResponse<AudiencePreviewDto>.SuccessResponse(
                await _announcements.PreviewAsync(request, ct), "Audience previewed"));

        [HttpPost("announcements")]
        [Authorize(Roles = AppRoles.JournalOversight)]
        public async Task<ActionResult<ApiResponse<AnnouncementDto>>> SendAnnouncement(
            [FromBody] AnnouncementRequest request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(ApiResponse<AnnouncementDto>.ErrorResponse("An announcement needs a title."));
            }

            var sent = await _announcements.SendAsync(request, UserId, UserName, ct);

            _logger.LogInformation(
                "Announcement {Id} sent by {User} to {Count} recipient(s).",
                sent.Id, UserId, sent.RecipientCount);

            return Ok(ApiResponse<AnnouncementDto>.SuccessResponse(sent, "Announcement sent"));
        }

        [HttpGet("announcements")]
        [Authorize(Roles = AppRoles.JournalOversight)]
        public async Task<ActionResult<ApiResponse<List<AnnouncementDto>>>> GetAnnouncements(
            [FromQuery] int limit = 50, CancellationToken ct = default) =>
            Ok(ApiResponse<List<AnnouncementDto>>.SuccessResponse(
                await _announcements.GetHistoryAsync(limit, ct), "Announcements retrieved"));
    }

    public class MarkReadRequest
    {
        /// <summary>Specific ids, or empty/null to mark everything read.</summary>
        public List<int>? Ids { get; set; }
    }

    public class SetLocaleRequest
    {
        public string Locale { get; set; } = "bs";
    }
}
