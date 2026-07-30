using Auth.Models.Data;
using Auth.Models.DTOs.Email;
using Auth.Models.DTOs.FLS;
using Auth.Models.Entities.FLS;
using Auth.Models.Enums.FLS;
using Auth.Models.Exceptions;
using Auth.Models.Request.FLS;
using Auth.Services.Interfaces.Email;
using Auth.Services.Interfaces.FLS;
using Auth.Services.Services.Email;
using Auth.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auth.Services.Services.FLS
{
    public class FLSNotificationService : IFLSNotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailDispatcher _dispatcher;
        private readonly IEmailTemplateRenderer _renderer;
        private readonly EmailOptions _emailOptions;
        private readonly ILogger<FLSNotificationService> _logger;

        public FLSNotificationService(
            ApplicationDbContext context,
            IEmailDispatcher dispatcher,
            IEmailTemplateRenderer renderer,
            IOptions<EmailOptions> emailOptions,
            ILogger<FLSNotificationService> logger)
        {
            _context = context;
            _dispatcher = dispatcher;
            _renderer = renderer;
            _emailOptions = emailOptions.Value;
            _logger = logger;
        }

        public async Task<List<SpeakerNotificationDto>> GetNotificationsAsync(int speakerProfileId, bool unreadOnly = false)
        {
            var query = _context.SpeakerNotifications
                .Where(n => n.SpeakerProfileId == speakerProfileId);

            if (unreadOnly)
                query = query.Where(n => !n.IsRead);

            var notifications = await query.OrderByDescending(n => n.CreatedAt).ToListAsync();
            return notifications.Select(MapToDto).ToList();
        }

        public async Task<bool> MarkAsReadAsync(int notificationId, int speakerProfileId)
        {
            var notification = await _context.SpeakerNotifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.SpeakerProfileId == speakerProfileId)
                ?? throw new NotFoundException("Notification not found.");

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> MarkAllReadAsync(int speakerProfileId)
        {
            var notifications = await _context.SpeakerNotifications
                .Where(n => n.SpeakerProfileId == speakerProfileId && !n.IsRead)
                .ToListAsync();

            foreach (var n in notifications)
            {
                n.IsRead = true;
                n.ReadAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<int> GetUnreadCountAsync(int speakerProfileId)
        {
            return await _context.SpeakerNotifications
                .CountAsync(n => n.SpeakerProfileId == speakerProfileId && !n.IsRead);
        }

        /// <summary>
        /// Creates in-app notifications and, when requested, emails them.
        ///
        /// Previously this loaded each speaker profile individually and called
        /// SaveChanges once per recipient (3 round trips per speaker). It now loads every
        /// target profile with its user in one query and persists once, so a broadcast to
        /// N speakers costs 2 queries instead of 3N.
        /// </summary>
        public async Task SendNotificationAsync(SendNotificationRequest request)
        {
            var profiles = await LoadTargetProfilesAsync(request.SpeakerProfileId);

            if (profiles.Count == 0)
            {
                _logger.LogWarning(
                    "SendNotification matched no speakers (speakerProfileId={Id}).", request.SpeakerProfileId);
                return;
            }

            if (profiles.Count > _emailOptions.MaxRecipientsPerCampaign)
            {
                throw new InvalidOperationException(
                    $"Refusing to notify {profiles.Count} speakers — the limit is " +
                    $"{_emailOptions.MaxRecipientsPerCampaign} (EMAIL_MAX_RECIPIENTS_PER_CAMPAIGN).");
            }

            var notifications = new List<SpeakerNotification>(profiles.Count);

            foreach (var profile in profiles)
            {
                notifications.Add(new SpeakerNotification
                {
                    SpeakerProfileId = profile.Id,
                    NotificationType = request.NotificationType,
                    Title = request.Title,
                    Message = request.Message,
                    EmailSent = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            _context.SpeakerNotifications.AddRange(notifications);
            await _context.SaveChangesAsync();

            if (!request.SendEmail) return;

            // Notification rows and their profiles are index-aligned by construction above.
            for (var i = 0; i < profiles.Count; i++)
            {
                var sent = await SendEmailForAsync(
                    profiles[i], request.Title, request.Message, request.Deadline, request.EmailProvider);

                notifications[i].EmailSent = sent;

                if (_emailOptions.SendDelayMs > 0 && i < profiles.Count - 1)
                    await Task.Delay(_emailOptions.SendDelayMs);
            }

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Notifies every speaker with missing uploads. Despite the name this used to only
        /// create in-app rows and never send a single email; it now does both, and the
        /// reminder body is personalised per speaker with the specific missing items.
        /// </summary>
        public async Task SendReminderEmailsAsync()
        {
            var profiles = await _context.SpeakerProfiles
                .Include(sp => sp.Uploads)
                .Include(sp => sp.User)
                .Where(sp => !sp.IsDeregistered)
                .ToListAsync();

            var required = new[]
            {
                UploadType.CV, UploadType.Picture, UploadType.Synopsis, UploadType.Presentation
            };

            var pending = new List<(SpeakerProfile Profile, SpeakerNotification Notification, string Body)>();

            foreach (var profile in profiles)
            {
                var missing = required
                    .Where(type => profile.Uploads.All(u => u.UploadType != type))
                    .Select(type => type.ToString())
                    .ToList();

                if (missing.Count == 0) continue;

                var body =
                    $"Hi {{{{firstName}}}},\n\n" +
                    $"We're still missing the following from your FLS {{{{year}}}} speaker profile:\n\n" +
                    string.Join("\n", missing.Select(m => $"• {m}")) + "\n\n" +
                    "Please upload the outstanding items in the speaker portal:\n" +
                    $"{TemplateVariables.PortalUrl}\n\n" +
                    "Thank you,\nFLS Team";

                var notification = new SpeakerNotification
                {
                    SpeakerProfileId = profile.Id,
                    NotificationType = FLSNotificationType.DeadlineApproaching,
                    Title = "Upload reminder — outstanding items",
                    Message = $"You still need to upload: {string.Join(", ", missing)}.",
                    CreatedAt = DateTime.UtcNow
                };

                pending.Add((profile, notification, body));
            }

            if (pending.Count == 0)
            {
                _logger.LogInformation("Reminder run: every active speaker has uploaded all required items.");
                return;
            }

            _context.SpeakerNotifications.AddRange(pending.Select(p => p.Notification));
            await _context.SaveChangesAsync();

            var sentCount = 0;
            for (var i = 0; i < pending.Count; i++)
            {
                var (profile, notification, body) = pending[i];

                var sent = await SendEmailForAsync(profile, "Upload reminder — outstanding items", body);
                notification.EmailSent = sent;
                if (sent) sentCount++;

                if (_emailOptions.SendDelayMs > 0 && i < pending.Count - 1)
                    await Task.Delay(_emailOptions.SendDelayMs);
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Reminder run: {Total} speakers had missing uploads, {Sent} emails delivered.",
                pending.Count, sentCount);
        }

        private async Task<List<SpeakerProfile>> LoadTargetProfilesAsync(int? speakerProfileId)
        {
            var query = _context.SpeakerProfiles
                .Include(sp => sp.User)
                .Where(sp => !sp.IsDeregistered);

            if (speakerProfileId.HasValue)
                query = query.Where(sp => sp.Id == speakerProfileId.Value);

            return await query.ToListAsync();
        }

        /// <summary>
        /// Renders and sends one notification email.
        ///
        /// This is the method that used to call <c>QueueEmailConfirmationAsync</c>, which
        /// pushed the message onto the account-confirmation queue — recipients got an email
        /// titled "Confirm Your Email Address" with the notification text jammed inside an
        /// href attribute, so the actual message was never visible. It now renders the
        /// proper template and dispatches it as a real email.
        /// </summary>
        private async Task<bool> SendEmailForAsync(
            SpeakerProfile profile,
            string subject,
            string body,
            string? deadline = null,
            string? providerKey = null)
        {
            var email = profile.User?.Email;
            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogWarning("Speaker profile {Id} has no email address; skipping.", profile.Id);
                return false;
            }

            var variables = TemplateVariables.ForSpeaker(profile, profile.User!, deadline);
            var rendered = _renderer.Render(subject, body, variables);

            var result = await _dispatcher.SendAsync(new OutboundEmail
            {
                ToEmail = email,
                ToName = variables["fullName"],
                Subject = rendered.Subject,
                HtmlBody = rendered.HtmlBody,
                TextBody = rendered.TextBody,
                Tag = "fls-notification"
            }, providerKey);

            if (!result.Success)
            {
                _logger.LogWarning(
                    "Notification email to {Email} failed via {Provider}: {Error}",
                    email, result.Provider, result.Error);
            }

            return result.Success;
        }

        private static SpeakerNotificationDto MapToDto(SpeakerNotification n) => new()
        {
            Id = n.Id,
            NotificationType = n.NotificationType,
            Title = n.Title,
            Message = n.Message,
            IsRead = n.IsRead,
            EmailSent = n.EmailSent,
            CreatedAt = n.CreatedAt,
            ReadAt = n.ReadAt
        };
    }
}
