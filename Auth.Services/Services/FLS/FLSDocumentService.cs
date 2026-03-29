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
    public class FLSDocumentService : IFLSDocumentService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<FLSDocumentService> _logger;
        private readonly string _uploadBasePath;

        private const int MaxCommentsPerSpeakerPerDocument = 2;
        private const int MaxCommentWordCount = 500;

        private static readonly string[] AllowedDocumentMimeTypes =
        [
            "application/pdf",
            "image/jpeg", "image/png",
            "application/vnd.ms-powerpoint",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        ];

        public FLSDocumentService(ApplicationDbContext context, ILogger<FLSDocumentService> logger, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            var basePath = configuration["FLS_UPLOAD_PATH"]
                ?? Path.Combine(AppContext.BaseDirectory, "uploads", "fls");
            _uploadBasePath = Path.Combine(basePath, "documents");
        }

        public async Task<FLSDocumentDto> UploadDocumentAsync(IFormFile file, string title, FLSDocumentType documentType, string uploadedByUserId, int? targetSpeakerProfileId = null)
        {
            if (file == null || file.Length == 0)
                throw new ValidationException("File is empty.");

            if (!AllowedDocumentMimeTypes.Contains(file.ContentType.ToLower()))
                throw new ValidationException($"File type '{file.ContentType}' is not allowed.");

            Directory.CreateDirectory(_uploadBasePath);

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var storedFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(_uploadBasePath, storedFileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var document = new FLSDocument
            {
                DocumentType = documentType,
                Title = title,
                OriginalFileName = file.FileName,
                StoredFileName = storedFileName,
                FilePath = filePath,
                FileSizeBytes = file.Length,
                MimeType = file.ContentType,
                UploadedByUserId = uploadedByUserId,
                TargetSpeakerProfileId = targetSpeakerProfileId,
                IsActive = true,
                UploadedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.FLSDocuments.Add(document);
            await _context.SaveChangesAsync();

            _logger.LogInformation("FLS Document uploaded: {Title} ({Type}) by {UserId}", title, documentType, uploadedByUserId);
            return MapToDto(document);
        }

        public async Task<List<FLSDocumentDto>> GetDocumentsAsync(FLSDocumentType? documentType = null, int? targetSpeakerProfileId = null)
        {
            var query = _context.FLSDocuments
                .Include(d => d.Comments)
                .Where(d => d.IsActive)
                .AsQueryable();

            if (documentType.HasValue)
                query = query.Where(d => d.DocumentType == documentType.Value);

            if (targetSpeakerProfileId.HasValue)
                query = query.Where(d => d.TargetSpeakerProfileId == null || d.TargetSpeakerProfileId == targetSpeakerProfileId.Value);

            var documents = await query.OrderByDescending(d => d.UploadedAt).ToListAsync();
            return documents.Select(MapToDto).ToList();
        }

        public async Task<FLSDocumentDto?> GetDocumentByIdAsync(int documentId)
        {
            var doc = await _context.FLSDocuments
                .Include(d => d.Comments)
                .FirstOrDefaultAsync(d => d.Id == documentId);

            return doc == null ? null : MapToDto(doc);
        }

        public async Task<bool> DeleteDocumentAsync(int documentId)
        {
            var doc = await _context.FLSDocuments.FindAsync(documentId)
                ?? throw new NotFoundException("Document not found.");

            if (File.Exists(doc.FilePath))
                File.Delete(doc.FilePath);

            doc.IsActive = false;
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<(byte[] FileBytes, string ContentType, string FileName)> DownloadDocumentAsync(int documentId)
        {
            var doc = await _context.FLSDocuments.FindAsync(documentId)
                ?? throw new NotFoundException("Document not found.");

            if (!File.Exists(doc.FilePath))
                throw new NotFoundException("File not found on disk.");

            var bytes = await File.ReadAllBytesAsync(doc.FilePath);
            return (bytes, doc.MimeType, doc.OriginalFileName);
        }

        public async Task<SpeakerCommentDto> AddCommentAsync(int speakerProfileId, AddSpeakerCommentRequest request)
        {
            var doc = await _context.FLSDocuments.FindAsync(request.FLSDocumentId)
                ?? throw new NotFoundException("Document not found.");

            if (!doc.IsActive)
                throw new ValidationException("Cannot comment on an inactive document.");

            var existingCount = await GetCommentCountForSpeakerAsync(speakerProfileId, request.FLSDocumentId);
            if (existingCount >= MaxCommentsPerSpeakerPerDocument)
                throw new ValidationException($"Comment limit reached. You may submit a maximum of {MaxCommentsPerSpeakerPerDocument} comments per document.");

            var wordCount = CountWords(request.Content);
            if (wordCount > MaxCommentWordCount)
                throw new ValidationException($"Comment exceeds the {MaxCommentWordCount}-word limit. Your comment has {wordCount} words.");

            var comment = new SpeakerComment
            {
                SpeakerProfileId = speakerProfileId,
                FLSDocumentId = request.FLSDocumentId,
                IsApproval = request.IsApproval,
                Content = request.Content,
                CreatedAt = DateTime.UtcNow
            };

            _context.SpeakerComments.Add(comment);
            await _context.SaveChangesAsync();

            return new SpeakerCommentDto
            {
                Id = comment.Id,
                SpeakerProfileId = comment.SpeakerProfileId,
                FLSDocumentId = comment.FLSDocumentId,
                IsApproval = comment.IsApproval,
                Content = comment.Content,
                CreatedAt = comment.CreatedAt
            };
        }

        public async Task<List<SpeakerCommentDto>> GetCommentsForDocumentAsync(int documentId)
        {
            var comments = await _context.SpeakerComments
                .Include(c => c.SpeakerProfile)
                .Where(c => c.FLSDocumentId == documentId)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            var result = new List<SpeakerCommentDto>();
            foreach (var c in comments)
            {
                result.Add(new SpeakerCommentDto
                {
                    Id = c.Id,
                    SpeakerProfileId = c.SpeakerProfileId,
                    FLSDocumentId = c.FLSDocumentId,
                    IsApproval = c.IsApproval,
                    Content = c.Content,
                    CreatedAt = c.CreatedAt
                });
            }
            return result;
        }

        public async Task<int> GetCommentCountForSpeakerAsync(int speakerProfileId, int documentId)
        {
            return await _context.SpeakerComments
                .CountAsync(c => c.SpeakerProfileId == speakerProfileId && c.FLSDocumentId == documentId);
        }

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static FLSDocumentDto MapToDto(FLSDocument d) => new()
        {
            Id = d.Id,
            DocumentType = d.DocumentType,
            DocumentTypeLabel = d.DocumentType.ToString(),
            Title = d.Title,
            OriginalFileName = d.OriginalFileName,
            FileSizeBytes = d.FileSizeBytes,
            MimeType = d.MimeType,
            UploadedByUserId = d.UploadedByUserId,
            TargetSpeakerProfileId = d.TargetSpeakerProfileId,
            IsActive = d.IsActive,
            UploadedAt = d.UploadedAt,
            CommentCount = d.Comments.Count,
            Comments = d.Comments.Select(c => new SpeakerCommentDto
            {
                Id = c.Id,
                SpeakerProfileId = c.SpeakerProfileId,
                FLSDocumentId = c.FLSDocumentId,
                IsApproval = c.IsApproval,
                Content = c.Content,
                CreatedAt = c.CreatedAt
            }).ToList()
        };
    }
}
