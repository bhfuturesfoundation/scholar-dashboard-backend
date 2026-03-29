using Auth.Models.Data;
using Auth.Models.DTOs.FLS;
using Auth.Models.Entities.FLS;
using Auth.Models.Enums.FLS;
using Auth.Models.Exceptions;
using Auth.Models.Request.FLS;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.FLS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.FLS
{
    public class FLSNotificationService : IFLSNotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<FLSNotificationService> _logger;

        public FLSNotificationService(
            ApplicationDbContext context,
            IEmailService emailService,
            ILogger<FLSNotificationService> logger)
        {
            _context = context;
            _emailService = emailService;
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

        public async Task SendNotificationAsync(SendNotificationRequest request)
        {
            if (request.SpeakerProfileId.HasValue)
            {
                await CreateAndSendForSpeaker(request.SpeakerProfileId.Value, request);
            }
            else
            {
                // Broadcast to all active speakers
                var activeProfiles = await _context.SpeakerProfiles
                    .Include(sp => sp.User)
                    .Where(sp => !sp.IsDeregistered)
                    .ToListAsync();

                foreach (var profile in activeProfiles)
                    await CreateAndSendForSpeaker(profile.Id, request);
            }
        }

        public async Task SendReminderEmailsAsync()
        {
            var profiles = await _context.SpeakerProfiles
                .Include(sp => sp.Uploads)
                .Where(sp => !sp.IsDeregistered)
                .ToListAsync();

            foreach (var profile in profiles)
            {
                var missing = new List<string>();

                var types = new[] { UploadType.CV, UploadType.Picture, UploadType.Synopsis, UploadType.Presentation };
                foreach (var type in types)
                {
                    if (!profile.Uploads.Any(u => u.UploadType == type))
                        missing.Add(type.ToString());
                }

                if (!missing.Any()) continue;

                var reminder = new SpeakerNotification
                {
                    SpeakerProfileId = profile.Id,
                    NotificationType = FLSNotificationType.DeadlineApproaching,
                    Title = "Upload Reminder",
                    Message = $"You still need to upload the following: {string.Join(", ", missing)}. Please complete your profile.",
                    CreatedAt = DateTime.UtcNow
                };

                _context.SpeakerNotifications.Add(reminder);
                _logger.LogInformation("Reminder queued for speaker profile {Id}: {Missing}", profile.Id, string.Join(", ", missing));
            }

            await _context.SaveChangesAsync();
        }

        private async Task CreateAndSendForSpeaker(int speakerProfileId, SendNotificationRequest request)
        {
            var notification = new SpeakerNotification
            {
                SpeakerProfileId = speakerProfileId,
                NotificationType = request.NotificationType,
                Title = request.Title,
                Message = request.Message,
                EmailSent = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.SpeakerNotifications.Add(notification);
            await _context.SaveChangesAsync();

            if (request.SendEmail)
            {
                var profile = await _context.SpeakerProfiles
                    .Include(sp => sp.User)
                    .FirstOrDefaultAsync(sp => sp.Id == speakerProfileId);

                if (profile?.User?.Email != null)
                {
                    _emailService.QueueEmailConfirmationAsync(profile.User.Email, $"FLS Notification: {request.Title}\n\n{request.Message}");
                    notification.EmailSent = true;
                    await _context.SaveChangesAsync();
                }
            }
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
