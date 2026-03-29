using Auth.Models.Data;
using Auth.Models.DTOs.FLS;
using Auth.Models.Entities.FLS;
using Auth.Models.Enums.FLS;
using Auth.Models.Exceptions;
using Auth.Models.Request.FLS;
using Auth.Services.Interfaces.FLS;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.FLS
{
    public class FLSUploadService : IFLSUploadService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<FLSUploadService> _logger;
        private readonly string _uploadBasePath;

        // File size limits in bytes
        private const long CvMaxSize = 5 * 1024 * 1024;         // 5 MB
        private const long PictureMaxSize = 5 * 1024 * 1024;    // 5 MB
        private const long SynopsisMaxSize = 5 * 1024 * 1024;   // 5 MB
        private const long PresentationMaxSize = 10 * 1024 * 1024; // 10 MB
        private const int MaxSynopsisWordCount = 250;

        private static readonly Dictionary<UploadType, string[]> AllowedMimeTypes = new()
        {
            [UploadType.CV] = ["application/pdf", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
            [UploadType.Picture] = ["image/jpeg", "image/png", "image/webp"],
            [UploadType.Synopsis] = ["application/pdf", "text/plain", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
            [UploadType.Presentation] = ["application/pdf", "application/vnd.ms-powerpoint", "application/vnd.openxmlformats-officedocument.presentationml.presentation"]
        };

        private static readonly Dictionary<UploadType, string[]> AllowedExtensions = new()
        {
            [UploadType.CV] = [".pdf", ".doc", ".docx"],
            [UploadType.Picture] = [".jpg", ".jpeg", ".png", ".webp"],
            [UploadType.Synopsis] = [".pdf", ".txt", ".doc", ".docx"],
            [UploadType.Presentation] = [".pdf", ".ppt", ".pptx"]
        };

        public FLSUploadService(ApplicationDbContext context, ILogger<FLSUploadService> logger, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _uploadBasePath = configuration["FLS_UPLOAD_PATH"]
                ?? Path.Combine(AppContext.BaseDirectory, "uploads", "fls");
        }

        public async Task<SpeakerUploadDto> UploadFileAsync(int speakerProfileId, IFormFile file, UploadType uploadType)
        {
            var profile = await _context.SpeakerProfiles.FindAsync(speakerProfileId)
                ?? throw new NotFoundException("Speaker profile not found.");

            ValidateFile(file, uploadType);

            // Word count validation for synopsis text files
            int? wordCount = null;
            if (uploadType == UploadType.Synopsis && file.ContentType == "text/plain")
            {
                using var reader = new StreamReader(file.OpenReadStream());
                var text = await reader.ReadToEndAsync();
                wordCount = CountWords(text);
                if (wordCount > MaxSynopsisWordCount)
                    throw new ValidationException($"Synopsis exceeds the {MaxSynopsisWordCount}-word limit. Your synopsis has {wordCount} words.");
            }

            // Build folder: uploads/fls/{speakerProfileId}/{uploadType}/
            var folder = Path.Combine(_uploadBasePath, speakerProfileId.ToString(), uploadType.ToString().ToLower());
            Directory.CreateDirectory(folder);

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var storedFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(folder, storedFileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Replace any existing upload of the same type (keep history via versioning)
            var existing = await _context.SpeakerUploads
                .Where(u => u.SpeakerProfileId == speakerProfileId && u.UploadType == uploadType)
                .OrderByDescending(u => u.UploadedAt)
                .FirstOrDefaultAsync();

            if (existing != null)
            {
                // Delete old physical file
                if (File.Exists(existing.FilePath))
                    File.Delete(existing.FilePath);

                _context.SpeakerUploads.Remove(existing);
            }

            var upload = new SpeakerUpload
            {
                SpeakerProfileId = speakerProfileId,
                UploadType = uploadType,
                Status = uploadType == UploadType.Presentation ? UploadStatus.Pending : UploadStatus.Approved,
                OriginalFileName = file.FileName,
                StoredFileName = storedFileName,
                FilePath = filePath,
                FileSizeBytes = file.Length,
                MimeType = file.ContentType,
                WordCount = wordCount,
                UploadedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.SpeakerUploads.Add(upload);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Speaker {Id} uploaded {Type}: {FileName}", speakerProfileId, uploadType, file.FileName);

            return MapToDto(upload);
        }

        public async Task<SpeakerUploadDto?> GetUploadAsync(int uploadId)
        {
            var upload = await _context.SpeakerUploads.FindAsync(uploadId);
            return upload == null ? null : MapToDto(upload);
        }

        public async Task<List<SpeakerUploadDto>> GetUploadsForSpeakerAsync(int speakerProfileId)
        {
            var uploads = await _context.SpeakerUploads
                .Where(u => u.SpeakerProfileId == speakerProfileId)
                .OrderBy(u => u.UploadType)
                .ThenByDescending(u => u.UploadedAt)
                .ToListAsync();

            return uploads.Select(MapToDto).ToList();
        }

        public async Task<SpeakerUploadDto> VerifyUploadAsync(int uploadId, VerifyUploadRequest request, string adminUserId)
        {
            var upload = await _context.SpeakerUploads.FindAsync(uploadId)
                ?? throw new NotFoundException("Upload not found.");

            if (upload.UploadType != UploadType.Presentation)
                throw new ValidationException("Only presentation uploads require manual verification.");

            upload.Status = request.Status;
            upload.VerifiedByUserId = adminUserId;
            upload.VerifiedAt = DateTime.UtcNow;
            upload.VerificationNotes = request.VerificationNotes;
            upload.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Upload {Id} verified as {Status} by admin {AdminId}", uploadId, request.Status, adminUserId);

            return MapToDto(upload);
        }

        public async Task<bool> DeleteUploadAsync(int uploadId, int speakerProfileId)
        {
            var upload = await _context.SpeakerUploads
                .FirstOrDefaultAsync(u => u.Id == uploadId && u.SpeakerProfileId == speakerProfileId)
                ?? throw new NotFoundException("Upload not found.");

            if (File.Exists(upload.FilePath))
                File.Delete(upload.FilePath);

            _context.SpeakerUploads.Remove(upload);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<(byte[] FileBytes, string ContentType, string FileName)> DownloadFileAsync(int uploadId)
        {
            var upload = await _context.SpeakerUploads.FindAsync(uploadId)
                ?? throw new NotFoundException("Upload not found.");

            if (!File.Exists(upload.FilePath))
                throw new NotFoundException("File not found on disk.");

            var bytes = await File.ReadAllBytesAsync(upload.FilePath);
            return (bytes, upload.MimeType, upload.OriginalFileName);
        }

        public async Task<SpeakerCompletionStatusDto> GetCompletionStatusAsync(int speakerProfileId)
        {
            var uploads = await _context.SpeakerUploads
                .Where(u => u.SpeakerProfileId == speakerProfileId)
                .ToListAsync();

            var latest = uploads
                .GroupBy(u => u.UploadType)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(u => u.UploadedAt).First());

            return new SpeakerCompletionStatusDto
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
        }

        private static void ValidateFile(IFormFile file, UploadType uploadType)
        {
            if (file == null || file.Length == 0)
                throw new ValidationException("File is empty.");

            var maxSize = uploadType switch
            {
                UploadType.Presentation => PresentationMaxSize,
                _ => CvMaxSize
            };

            if (file.Length > maxSize)
                throw new ValidationException($"File size exceeds the maximum allowed size of {maxSize / (1024 * 1024)} MB.");

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions[uploadType].Contains(extension))
            {
                var allowed = string.Join(", ", AllowedExtensions[uploadType]);
                throw new ValidationException($"File type not allowed for {uploadType}. Allowed types: {allowed}");
            }

            if (AllowedMimeTypes.TryGetValue(uploadType, out var mimes) && !mimes.Contains(file.ContentType.ToLower()))
                throw new ValidationException($"Invalid MIME type '{file.ContentType}' for {uploadType}.");
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static SpeakerUploadDto MapToDto(SpeakerUpload u) => new()
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
    }
}
