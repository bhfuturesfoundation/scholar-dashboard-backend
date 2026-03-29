using Auth.Models.Data;
using Auth.Models.DTOs.FLS;
using Auth.Models.Entities;
using Auth.Models.Entities.FLS;
using Auth.Models.Enums.FLS;
using Auth.Models.Exceptions;
using Auth.Models.Request.FLS;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.FLS
{
    public class FLSSpeakerService : IFLSSpeakerService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<FLSSpeakerService> _logger;

        public FLSSpeakerService(ApplicationDbContext context, UserManager<User> userManager, ILogger<FLSSpeakerService> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<SpeakerProfileDto> RegisterSpeakerAsync(FLSRegisterRequest request)
        {
            var existingUser = await _userManager.FindByEmailAsync(request.Email);
            if (existingUser != null)
                throw new ConflictException("A user with this email already exists.");

            var user = new User
            {
                UserName = request.Email,
                Email = request.Email,
                FirstName = request.FirstName,
                LastName = request.LastName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true,
                MustChangePassword = false
            };

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                throw new ValidationException($"Failed to create user: {errors}");
            }

            await _userManager.AddToRoleAsync(user, "FLSSpeaker");

            var profile = new SpeakerProfile
            {
                UserId = user.Id,
                SpeakerType = request.SpeakerType,
                Organization = request.Organization,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.SpeakerProfiles.Add(profile);
            await _context.SaveChangesAsync();

            _logger.LogInformation("FLS Speaker registered: {Email} (ProfileId: {Id})", request.Email, profile.Id);

            return await MapToDto(profile, user);
        }

        public async Task<SpeakerProfileDto?> GetProfileByUserIdAsync(string userId)
        {
            var profile = await _context.SpeakerProfiles
                .Include(sp => sp.Uploads)
                .FirstOrDefaultAsync(sp => sp.UserId == userId);

            if (profile == null) return null;

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return null;

            return await MapToDto(profile, user);
        }

        public async Task<SpeakerProfileDto?> GetProfileByIdAsync(int speakerProfileId)
        {
            var profile = await _context.SpeakerProfiles
                .Include(sp => sp.Uploads)
                .FirstOrDefaultAsync(sp => sp.Id == speakerProfileId);

            if (profile == null) return null;

            var user = await _userManager.FindByIdAsync(profile.UserId);
            if (user == null) return null;

            return await MapToDto(profile, user);
        }

        public async Task<SpeakerProfileDto> UpdateProfileAsync(int speakerProfileId, UpdateSpeakerProfileRequest request)
        {
            var profile = await _context.SpeakerProfiles
                .Include(sp => sp.Uploads)
                .FirstOrDefaultAsync(sp => sp.Id == speakerProfileId)
                ?? throw new NotFoundException("Speaker profile not found.");

            if (request.Organization != null) profile.Organization = request.Organization;
            if (request.Biography != null) profile.Biography = request.Biography;
            if (request.SpeakerType.HasValue) profile.SpeakerType = request.SpeakerType.Value;
            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var user = await _userManager.FindByIdAsync(profile.UserId)
                ?? throw new NotFoundException("Associated user not found.");

            return await MapToDto(profile, user);
        }

        public async Task<bool> UpdateAccommodationAsync(int speakerProfileId, UpdateSpeakerAccommodationRequest request)
        {
            var profile = await _context.SpeakerProfiles.FindAsync(speakerProfileId)
                ?? throw new NotFoundException("Speaker profile not found.");

            profile.AccommodationBooked = request.AccommodationBooked;
            profile.FlightTicketBooked = request.FlightTicketBooked;
            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeregisterSpeakerAsync(int speakerProfileId, DeregisterSpeakerRequest request, string adminUserId)
        {
            var profile = await _context.SpeakerProfiles.FindAsync(speakerProfileId)
                ?? throw new NotFoundException("Speaker profile not found.");

            profile.IsDeregistered = true;
            profile.DeregisteredAt = DateTime.UtcNow;
            profile.DeregistrationReason = request.Reason;
            profile.UpdatedAt = DateTime.UtcNow;

            // Deactivate the user account
            var user = await _userManager.FindByIdAsync(profile.UserId);
            if (user != null)
            {
                user.IsActive = false;
                await _userManager.UpdateAsync(user);
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Speaker profile {Id} deregistered by admin {AdminId}", speakerProfileId, adminUserId);
            return true;
        }

        public async Task<bool> ReactivateSpeakerAsync(int speakerProfileId)
        {
            var profile = await _context.SpeakerProfiles.FindAsync(speakerProfileId)
                ?? throw new NotFoundException("Speaker profile not found.");

            profile.IsDeregistered = false;
            profile.DeregisteredAt = null;
            profile.DeregistrationReason = null;
            profile.UpdatedAt = DateTime.UtcNow;

            var user = await _userManager.FindByIdAsync(profile.UserId);
            if (user != null)
            {
                user.IsActive = true;
                await _userManager.UpdateAsync(user);
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> SetModificationDeadlineAsync(int speakerProfileId, SetModificationDeadlineRequest request)
        {
            var profile = await _context.SpeakerProfiles.FindAsync(speakerProfileId)
                ?? throw new NotFoundException("Speaker profile not found.");

            profile.ModificationDeadline = request.ModificationDeadline;
            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CanModifyUploadsAsync(int speakerProfileId)
        {
            var profile = await _context.SpeakerProfiles.FindAsync(speakerProfileId);
            if (profile == null || profile.IsDeregistered) return false;
            if (!profile.ModificationDeadline.HasValue) return true;
            return DateTime.UtcNow <= profile.ModificationDeadline.Value;
        }

        private async Task<SpeakerProfileDto> MapToDto(SpeakerProfile profile, User user)
        {
            var uploads = profile.Uploads.Select(MapUploadToDto).ToList();
            var completionStatus = BuildCompletionStatus(profile.Uploads.ToList());

            return new SpeakerProfileDto
            {
                Id = profile.Id,
                UserId = profile.UserId,
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                Organization = profile.Organization,
                Biography = profile.Biography,
                SpeakerType = profile.SpeakerType,
                SpeakerTypeLabel = profile.SpeakerType.ToString(),
                IsDeregistered = profile.IsDeregistered,
                DeregisteredAt = profile.DeregisteredAt,
                DeregistrationReason = profile.DeregistrationReason,
                AccommodationBooked = profile.AccommodationBooked,
                FlightTicketBooked = profile.FlightTicketBooked,
                ModificationDeadline = profile.ModificationDeadline,
                CreatedAt = profile.CreatedAt,
                Uploads = uploads,
                CompletionStatus = completionStatus
            };
        }

        private static SpeakerUploadDto MapUploadToDto(SpeakerUpload u) => new()
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
        };

        private static SpeakerCompletionStatusDto BuildCompletionStatus(List<SpeakerUpload> uploads)
        {
            var latest = uploads
                .GroupBy(u => u.UploadType)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(u => u.UploadedAt).First());

            return new SpeakerCompletionStatusDto
            {
                CVUploaded = latest.ContainsKey(UploadType.CV),
                PictureUploaded = latest.ContainsKey(UploadType.Picture),
                SynopsisUploaded = latest.ContainsKey(UploadType.Synopsis),
                PresentationUploaded = latest.ContainsKey(UploadType.Presentation),
                PresentationVerified = latest.TryGetValue(UploadType.Presentation, out var pres) && pres.Status == UploadStatus.Approved,
                CVStatus = latest.TryGetValue(UploadType.CV, out var cv) ? cv.Status : UploadStatus.NotUploaded,
                PictureStatus = latest.TryGetValue(UploadType.Picture, out var pic) ? pic.Status : UploadStatus.NotUploaded,
                SynopsisStatus = latest.TryGetValue(UploadType.Synopsis, out var syn) ? syn.Status : UploadStatus.NotUploaded,
                PresentationStatus = latest.TryGetValue(UploadType.Presentation, out var pr) ? pr.Status : UploadStatus.NotUploaded
            };
        }
    }
}
