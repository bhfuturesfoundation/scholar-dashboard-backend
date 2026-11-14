using Auth.Models.Constants;
using Auth.Models.Data;
using Auth.Models.DTOs.Resources;
using Auth.Models.Entities.Resources;
using Auth.Models.Enums.Scholars;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Auth.Services.Interfaces.Notifications;
using Auth.Services.Interfaces.Resources;
using Microsoft.EntityFrameworkCore;

namespace Auth.Services.Services.Resources
{
    /// <inheritdoc cref="IResourceDocumentService"/>
    public class ResourceDocumentService : IResourceDocumentService
    {
        public const long MaxFileBytes = 20 * 1024 * 1024;

        /// <summary>
        /// What may be uploaded, with the content type it is served as. The type comes from
        /// this list, never from the browser, and the first bytes must match the format, so
        /// an HTML or SVG page renamed to .pdf can't be served back as a document.
        /// </summary>
        private static readonly Dictionary<string, (string ContentType, byte[] Magic)> AllowedTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".pdf"] = ("application/pdf", "%PDF"u8.ToArray()),
                [".png"] = ("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
                [".jpg"] = ("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF }),
                [".jpeg"] = ("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF }),
                [".docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "PK"u8.ToArray()),
                [".xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PK"u8.ToArray()),
                [".pptx"] = ("application/vnd.openxmlformats-officedocument.presentationml.presentation", "PK"u8.ToArray()),
            };

        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notifications;
        private readonly IAuditService _auditService;

        public ResourceDocumentService(
            ApplicationDbContext context, INotificationService notifications, IAuditService auditService)
        {
            _context = context;
            _notifications = notifications;
            _auditService = auditService;
        }

        public async Task<List<ResourceDocumentDto>> ListAsync(bool includeUnpublished, CancellationToken cancellationToken = default)
        {
            return await _context.ResourceDocuments
                .AsNoTracking()
                .Where(d => includeUnpublished || d.IsPublished)
                .OrderBy(d => d.SortOrder)
                .ThenByDescending(d => d.CreatedAt)
                .Select(d => new ResourceDocumentDto
                {
                    Id = d.Id,
                    Title = d.Title,
                    Description = d.Description,
                    FileName = d.FileName,
                    ContentType = d.ContentType,
                    SizeBytes = d.SizeBytes,
                    ExternalPath = d.ExternalPath,
                    IsPublished = d.IsPublished,
                    SortOrder = d.SortOrder,
                    CreatedAt = d.CreatedAt,
                    NotifiedAt = d.NotifiedAt
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<ResourceFile?> GetFileAsync(int id, bool includeUnpublished, CancellationToken cancellationToken = default)
        {
            return await _context.ResourceDocumentFiles
                .AsNoTracking()
                .Where(f => f.DocumentId == id && (includeUnpublished || f.Document!.IsPublished))
                .Select(f => new ResourceFile
                {
                    Content = f.Content,
                    ContentType = f.Document!.ContentType,
                    FileName = f.Document.FileName
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<ResourceDocumentDto> UploadAsync(
            string adminId, string title, string? description, string fileName, Stream content, long length,
            bool publish, bool notifyScholars, bool alsoEmail, CancellationToken cancellationToken = default)
        {
            title = title?.Trim() ?? string.Empty;
            if (title.Length == 0) throw new ValidationException("Give the document a title.");
            if (title.Length > 200) throw new ValidationException("The title can be at most 200 characters.");
            if (length <= 0) throw new ValidationException("The file is empty.");
            if (length > MaxFileBytes) throw new ValidationException("The file is larger than 20 MB.");

            var extension = Path.GetExtension(fileName);
            if (!AllowedTypes.TryGetValue(extension, out var type))
            {
                throw new ValidationException("Upload a PDF, Word, Excel or PowerPoint file, or a PNG or JPG image.");
            }

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            if (bytes.Length < type.Magic.Length || !bytes.AsSpan(0, type.Magic.Length).SequenceEqual(type.Magic))
            {
                throw new ValidationException("The file's contents don't match its type. Save it again and re-upload.");
            }

            var nextOrder = await _context.ResourceDocuments.Select(d => (int?)d.SortOrder).MaxAsync(cancellationToken) ?? 0;

            var document = new ResourceDocument
            {
                Title = title,
                Description = Clean(description),
                FileName = SafeFileName(fileName),
                ContentType = type.ContentType,
                SizeBytes = bytes.Length,
                IsPublished = publish,
                SortOrder = nextOrder + 1,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = adminId,
                File = new ResourceDocumentFile { Content = bytes }
            };

            _context.ResourceDocuments.Add(document);
            await _context.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync("Resources.Uploaded", adminId,
                $"Id={document.Id} File={document.FileName} Size={document.SizeBytes} Published={publish}");

            if (publish && notifyScholars) await NotifyAsync(document, alsoEmail, cancellationToken);

            return ToDto(document);
        }

        public async Task<ResourceDocumentDto> UpdateAsync(
            string adminId, int id, UpdateResourceDocumentRequest request, CancellationToken cancellationToken = default)
        {
            var document = await _context.ResourceDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken)
                ?? throw new NotFoundException("Document", id);

            var title = request.Title?.Trim();
            if (string.IsNullOrEmpty(title)) throw new ValidationException("Give the document a title.");
            if (title.Length > 200) throw new ValidationException("The title can be at most 200 characters.");

            document.Title = title;
            document.Description = Clean(request.Description);
            document.IsPublished = request.IsPublished;
            document.SortOrder = request.SortOrder;
            document.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync("Resources.Updated", adminId, $"Id={id} Published={request.IsPublished}");

            if (document.IsPublished && request.NotifyScholars) await NotifyAsync(document, request.AlsoEmail, cancellationToken);

            return ToDto(document);
        }

        public async Task DeleteAsync(string adminId, int id, CancellationToken cancellationToken = default)
        {
            var deleted = await _context.ResourceDocuments.Where(d => d.Id == id).ExecuteDeleteAsync(cancellationToken);
            if (deleted == 0) throw new NotFoundException("Document", id);

            await _auditService.LogAsync("Resources.Deleted", adminId, $"Id={id}");
        }

        /// <summary>
        /// Tells every current scholar once. Email is opt-in per document because a
        /// broadcast to every scholar can use a free email plan's monthly allowance in one go.
        /// </summary>
        private async Task NotifyAsync(ResourceDocument document, bool alsoEmail, CancellationToken cancellationToken)
        {
            if (document.NotifiedAt is not null) return;

            var scholarRoleId = await _context.Roles
                .Where(r => r.Name == AppRoles.User)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var scholarIds = await _context.Users
                .Where(u => u.IsActive
                    && u.ScholarStatus != ScholarStatus.Alumni
                    && u.ScholarStatus != ScholarStatus.Withdrawn
                    && _context.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == scholarRoleId))
                .Select(u => u.Id)
                .ToListAsync(cancellationToken);

            var requests = scholarIds.Select(userId => new CreateNotificationRequest
            {
                UserId = userId,
                MessageKey = NotificationKeys.ResourcePublished,
                Params = new Dictionary<string, string> { ["title"] = document.Title },
                DedupeKey = $"resource:{document.Id}",
                WantsEmail = alsoEmail,
                WantsPush = true
            }).ToList();

            await _notifications.CreateManyAsync(requests, cancellationToken);

            document.NotifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        private static string? Clean(string? value)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return null;
            return trimmed.Length <= 1000 ? trimmed : trimmed[..1000];
        }

        /// <summary>Keeps the name a person recognises, minus anything that could be read as a path or header.</summary>
        private static string SafeFileName(string fileName)
        {
            var name = Path.GetFileName(fileName);
            var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '"', '\r', '\n' }).ToHashSet();
            var cleaned = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim();
            if (cleaned.Length == 0) cleaned = "document" + Path.GetExtension(fileName);
            return cleaned.Length <= 255 ? cleaned : cleaned[^255..];
        }

        private static ResourceDocumentDto ToDto(ResourceDocument d) => new()
        {
            Id = d.Id,
            Title = d.Title,
            Description = d.Description,
            FileName = d.FileName,
            ContentType = d.ContentType,
            SizeBytes = d.SizeBytes,
            ExternalPath = d.ExternalPath,
            IsPublished = d.IsPublished,
            SortOrder = d.SortOrder,
            CreatedAt = d.CreatedAt,
            NotifiedAt = d.NotifiedAt
        };
    }
}
