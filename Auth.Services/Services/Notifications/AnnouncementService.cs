using Auth.Models.Constants;
using Auth.Models.Data;
using Auth.Models.DTOs.Notifications;
using Auth.Models.Entities;
using Auth.Models.Entities.Notifications;
using Auth.Models.Enums.Notifications;
using Auth.Services.Interfaces.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Notifications
{
    /// <inheritdoc cref="IAnnouncementService"/>
    public class AnnouncementService : IAnnouncementService
    {
        /// <summary>How many names the preview shows. Enough to recognise the filter is right.</summary>
        private const int SampleSize = 8;

        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notifications;
        private readonly IPushSender _push;
        private readonly ILogger<AnnouncementService> _logger;

        public AnnouncementService(
            ApplicationDbContext context,
            INotificationService notifications,
            IPushSender push,
            ILogger<AnnouncementService> logger)
        {
            _context = context;
            _notifications = notifications;
            _push = push;
            _logger = logger;
        }

        // ── Audience ──────────────────────────────────────────────────────────

        /// <summary>
        /// The audience query. Every supplied filter is ANDed, which is the reading a form
        /// implies: ticking "Senior" and "Mentor" means seniors who are mentors, not seniors
        /// plus all mentors.
        /// </summary>
        private IQueryable<User> BuildAudience(AnnouncementRequest request)
        {
            var query = _context.Users.AsNoTracking().AsQueryable();

            if (!request.IncludeInactive)
            {
                query = query.Where(u => u.IsActive);
            }

            if (request.TargetGenerationId is { } generationId)
            {
                query = query.Where(u => u.GenerationId == generationId);
            }

            if (request.TargetStatus is { } status)
            {
                query = query.Where(u => (int)u.ScholarStatus == status);
            }

            var roles = request.TargetRoles?
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .Distinct()
                .ToList();

            if (roles is { Count: > 0 })
            {
                // Joined through the Identity tables rather than via UserManager, which
                // would be one round trip per role and then an in-memory intersection.
                var roleIds = _context.Roles
                    .Where(r => r.Name != null && roles.Contains(r.Name))
                    .Select(r => r.Id);

                var userIds = _context.UserRoles
                    .Where(ur => roleIds.Contains(ur.RoleId))
                    .Select(ur => ur.UserId);

                query = query.Where(u => userIds.Contains(u.Id));
            }

            return query;
        }

        public async Task<AudiencePreviewDto> PreviewAsync(
            AnnouncementRequest request, CancellationToken cancellationToken = default)
        {
            var audience = BuildAudience(request);

            var recipients = await audience
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
                .ToListAsync(cancellationToken);

            var preview = new AudiencePreviewDto
            {
                TotalRecipients = recipients.Count,
                SampleNames = recipients
                    .Take(SampleSize)
                    .Select(u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } name
                        ? name
                        : u.Email ?? "(no name)")
                    .ToList()
            };

            if (recipients.Count == 0)
            {
                preview.Warnings.Add("No one matches these filters. Nothing would be sent.");
                return preview;
            }

            // Count the outbound channels against real preferences rather than assuming
            // everyone receives everything — otherwise the preview promises 200 emails and
            // 60 go out, which looks like a bug the first time it happens.
            if (request.SendEmail || request.SendPush)
            {
                var ids = recipients.Select(r => r.Id).ToList();

                var preferences = await _context.NotificationPreferences
                    .AsNoTracking()
                    .Where(p => ids.Contains(p.UserId))
                    .ToDictionaryAsync(p => p.UserId, cancellationToken);

                // Anyone with no row yet gets defaults, which is what the send path will
                // create for them.
                var fallback = new NotificationPreference();

                if (request.SendEmail)
                {
                    preview.EmailRecipients = ids.Count(id =>
                        (preferences.TryGetValue(id, out var p) ? p : fallback)
                        .Allows(NotificationCategory.Announcement, NotificationChannel.Email));
                }

                if (request.SendPush)
                {
                    if (!_push.IsConfigured)
                    {
                        preview.Warnings.Add("Push is requested but not configured on this deployment — no pushes will be sent.");
                    }
                    else
                    {
                        var optedIn = ids.Where(id =>
                            (preferences.TryGetValue(id, out var p) ? p : fallback)
                            .Allows(NotificationCategory.Announcement, NotificationChannel.Push))
                            .ToList();

                        preview.PushDevices = await _context.PushSubscriptions
                            .CountAsync(s => optedIn.Contains(s.UserId), cancellationToken);

                        if (preview.PushDevices == 0)
                        {
                            preview.Warnings.Add("No one in this audience has push enabled for announcements.");
                        }
                    }
                }
            }

            if (request.IncludeInactive)
            {
                preview.Warnings.Add(
                    "Inactive accounts are included. They are suppressed at email dispatch, so they will see this in the app only.");
            }

            return preview;
        }

        // ── Sending ───────────────────────────────────────────────────────────

        public async Task<AnnouncementDto> SendAsync(
            AnnouncementRequest request,
            string createdByUserId,
            string createdByName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                throw new ArgumentException("An announcement needs a title.", nameof(request));
            }

            var announcement = new Announcement
            {
                Title = request.Title.Trim(),
                Body = request.Body?.Trim() ?? string.Empty,
                ActionUrl = Normalise(request.ActionUrl),
                ActionLabel = string.IsNullOrWhiteSpace(request.ActionLabel) ? null : request.ActionLabel.Trim(),
                TargetRoles = request.TargetRoles is { Count: > 0 } ? string.Join(",", request.TargetRoles) : null,
                TargetGenerationId = request.TargetGenerationId,
                TargetStatus = request.TargetStatus,
                IncludeInactive = request.IncludeInactive,
                SendEmail = request.SendEmail,
                SendPush = request.SendPush,
                CreatedByUserId = createdByUserId,
                CreatedByName = createdByName,
                CreatedAt = DateTime.UtcNow
            };

            _context.Announcements.Add(announcement);
            await _context.SaveChangesAsync(cancellationToken);

            var recipientIds = await BuildAudience(request)
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            var requests = recipientIds.Select(id => new CreateNotificationRequest
            {
                UserId = id,
                MessageKey = NotificationKeys.Announcement,
                Params = new Dictionary<string, string>
                {
                    ["title"] = announcement.Title,
                    ["body"] = announcement.Body
                },
                ActionUrl = announcement.ActionUrl,

                // One per person per announcement, so a retried send cannot double-notify.
                DedupeKey = $"announcement:{announcement.Id}",
                WantsEmail = request.SendEmail,
                WantsPush = request.SendPush,
                AnnouncementId = announcement.Id
            }).ToList();

            var created = await _notifications.CreateManyAsync(requests, cancellationToken);

            announcement.SentAt = DateTime.UtcNow;
            announcement.RecipientCount = created;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Announcement {Id} \"{Title}\" sent to {Count} recipient(s) by {By}.",
                announcement.Id, announcement.Title, created, createdByName);

            return ToDto(announcement);
        }

        /// <summary>
        /// Keeps an action link relative. An announcement is composed by staff and rendered
        /// as a button in the app and in email; letting it carry an absolute URL would turn
        /// the compose box into an open redirect that any staff account could point anywhere.
        /// </summary>
        private static string? Normalise(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;

            var trimmed = url.Trim();
            if (!trimmed.StartsWith('/')) return null;

            // "//host" is protocol-relative and leaves the site.
            if (trimmed.StartsWith("//")) return null;

            return trimmed;
        }

        public async Task<List<AnnouncementDto>> GetHistoryAsync(
            int limit = 50, CancellationToken cancellationToken = default)
        {
            limit = Math.Clamp(limit, 1, 200);

            var announcements = await _context.Announcements
                .AsNoTracking()
                .OrderByDescending(a => a.CreatedAt)
                .Take(limit)
                .ToListAsync(cancellationToken);

            return announcements.Select(ToDto).ToList();
        }

        private static AnnouncementDto ToDto(Announcement a) => new()
        {
            Id = a.Id,
            Title = a.Title,
            Body = a.Body,
            ActionUrl = a.ActionUrl,
            ActionLabel = a.ActionLabel,
            TargetRoles = a.TargetRoles,
            TargetGenerationId = a.TargetGenerationId,
            TargetStatus = a.TargetStatus,
            IncludeInactive = a.IncludeInactive,
            SendEmail = a.SendEmail,
            SendPush = a.SendPush,
            CreatedByName = a.CreatedByName,
            CreatedAt = a.CreatedAt,
            SentAt = a.SentAt,
            RecipientCount = a.RecipientCount
        };
    }
}
