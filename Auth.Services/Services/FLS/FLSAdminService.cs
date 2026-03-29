using Auth.Models.Data;
using Auth.Models.DTOs.FLS;
using Auth.Models.Entities;
using Auth.Models.Enums.FLS;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Auth.Services.Services.FLS
{
    public class FLSAdminService : IFLSAdminService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IFLSSpeakerService _speakerService;
        private readonly ILogger<FLSAdminService> _logger;

        public FLSAdminService(
            ApplicationDbContext context,
            UserManager<User> userManager,
            IFLSSpeakerService speakerService,
            ILogger<FLSAdminService> logger)
        {
            _context = context;
            _userManager = userManager;
            _speakerService = speakerService;
            _logger = logger;
        }

        public async Task<List<SpeakerOverviewDto>> GetAllSpeakersAsync(bool includeDeregistered = false)
        {
            var profiles = await _context.SpeakerProfiles
                .Include(sp => sp.Uploads)
                .Include(sp => sp.Notifications)
                .Where(sp => includeDeregistered || !sp.IsDeregistered)
                .OrderBy(sp => sp.CreatedAt)
                .ToListAsync();

            var result = new List<SpeakerOverviewDto>();
            foreach (var profile in profiles)
            {
                var user = await _userManager.FindByIdAsync(profile.UserId);
                if (user == null) continue;

                var latest = profile.Uploads
                    .GroupBy(u => u.UploadType)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(u => u.UploadedAt).First());

                var completionStatus = new SpeakerCompletionStatusDto
                {
                    CVUploaded = latest.ContainsKey(UploadType.CV),
                    PictureUploaded = latest.ContainsKey(UploadType.Picture),
                    SynopsisUploaded = latest.ContainsKey(UploadType.Synopsis),
                    PresentationUploaded = latest.ContainsKey(UploadType.Presentation),
                    PresentationVerified = latest.TryGetValue(UploadType.Presentation, out var p) && p.Status == UploadStatus.Approved,
                    CVStatus = latest.TryGetValue(UploadType.CV, out var cv) ? cv.Status : UploadStatus.NotUploaded,
                    PictureStatus = latest.TryGetValue(UploadType.Picture, out var pic) ? pic.Status : UploadStatus.NotUploaded,
                    SynopsisStatus = latest.TryGetValue(UploadType.Synopsis, out var syn) ? syn.Status : UploadStatus.NotUploaded,
                    PresentationStatus = latest.TryGetValue(UploadType.Presentation, out var pr) ? pr.Status : UploadStatus.NotUploaded
                };

                result.Add(new SpeakerOverviewDto
                {
                    Id = profile.Id,
                    UserId = profile.UserId,
                    Email = user.Email ?? string.Empty,
                    FirstName = user.FirstName ?? string.Empty,
                    LastName = user.LastName ?? string.Empty,
                    Organization = profile.Organization,
                    SpeakerType = profile.SpeakerType,
                    SpeakerTypeLabel = profile.SpeakerType.ToString(),
                    IsDeregistered = profile.IsDeregistered,
                    AccommodationBooked = profile.AccommodationBooked,
                    FlightTicketBooked = profile.FlightTicketBooked,
                    CreatedAt = profile.CreatedAt,
                    CompletionStatus = completionStatus,
                    PendingNotifications = profile.Notifications.Count(n => !n.IsRead)
                });
            }

            return result;
        }

        public async Task<SpeakerProfileDto?> GetSpeakerDetailsAsync(int speakerProfileId)
        {
            return await _speakerService.GetProfileByIdAsync(speakerProfileId);
        }

        public async Task<byte[]> ExportSpeakersAsCsvAsync()
        {
            var speakers = await GetAllSpeakersAsync(includeDeregistered: true);

            var sb = new StringBuilder();
            sb.AppendLine("Id,FirstName,LastName,Email,Organization,SpeakerType,IsDeregistered,AccommodationBooked,FlightTicketBooked,CVStatus,PictureStatus,SynopsisStatus,PresentationStatus,PresentationVerified,RegisteredAt");

            foreach (var s in speakers)
            {
                sb.AppendLine($"{s.Id},{Escape(s.FirstName)},{Escape(s.LastName)},{Escape(s.Email)},{Escape(s.Organization ?? "")}," +
                              $"{s.SpeakerTypeLabel},{s.IsDeregistered},{s.AccommodationBooked},{s.FlightTicketBooked}," +
                              $"{s.CompletionStatus.CVStatus},{s.CompletionStatus.PictureStatus},{s.CompletionStatus.SynopsisStatus}," +
                              $"{s.CompletionStatus.PresentationStatus},{s.CompletionStatus.PresentationVerified},{s.CreatedAt:yyyy-MM-dd HH:mm:ss}");
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public async Task<List<SpeakerUploadDto>> GetPendingUploadsAsync()
        {
            var uploads = await _context.SpeakerUploads
                .Where(u => u.Status == UploadStatus.Pending)
                .OrderBy(u => u.UploadedAt)
                .ToListAsync();

            return uploads.Select(u => new SpeakerUploadDto
            {
                Id = u.Id,
                UploadType = u.UploadType,
                UploadTypeLabel = u.UploadType.ToString(),
                Status = u.Status,
                StatusLabel = u.Status.ToString(),
                OriginalFileName = u.OriginalFileName,
                FileSizeBytes = u.FileSizeBytes,
                MimeType = u.MimeType,
                WordCount = u.WordCount,
                VerificationNotes = u.VerificationNotes,
                VerifiedAt = u.VerifiedAt,
                UploadedAt = u.UploadedAt,
                UpdatedAt = u.UpdatedAt
            }).ToList();
        }

        private static string Escape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}
