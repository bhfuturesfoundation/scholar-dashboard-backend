using System.Security.Cryptography;
using Auth.Models.Data;
using Auth.Models.DTOs.Account;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Auth.Services.Services
{
    /// <inheritdoc cref="IAvatarService"/>
    public class AvatarService : IAvatarService
    {
        /// <summary>
        /// Checked against <c>IFormFile.Length</c> before a single byte is decoded.
        ///
        /// The order matters more than the number. Decoding is where all the cost and all the
        /// risk live, so the cheap check that needs nothing but a header has to come first —
        /// otherwise the limit is enforced only after the expensive thing already happened.
        /// </summary>
        private const long MaxUploadBytes = 5 * 1024 * 1024;

        /// <summary>
        /// Output edge, in pixels. 256 rather than 128 so the picture still looks right on a
        /// high-density screen, where CSS pixels are not device pixels and a 128px image in a
        /// 64px box is already only just sharp enough.
        /// </summary>
        private const int OutputSize = 256;

        /// <summary>
        /// Refuses images whose *decoded* pixel count is absurd, read from the header before
        /// decoding.
        ///
        /// This is the decompression-bomb guard, and it is a separate concern from the 5 MB
        /// limit: compression ratio is unbounded, so a small, perfectly valid PNG of one flat
        /// colour can decode to hundreds of megabytes of pixels. The byte limit does not
        /// constrain that at all. 40 megapixels sits above any real camera this will see while
        /// capping the decode buffer at roughly 160 MB — which a Railway container can survive
        /// and a deliberately crafted 200-megapixel file could not.
        /// </summary>
        private const long MaxPixels = 40_000_000;

        /// <summary>
        /// Quality 80 is the usual WebP sweet spot: visually indistinguishable from 100 at this
        /// size while landing around 10–15 KB per avatar, which is what keeps the whole table
        /// in single-digit megabytes.
        /// </summary>
        private static readonly WebpEncoder Encoder = new()
        {
            Quality = 80,
            FileFormat = WebpFileFormatType.Lossy
        };

        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;
        private readonly ILogger<AvatarService> _logger;

        public AvatarService(
            ApplicationDbContext context,
            IAuditService auditService,
            ILogger<AvatarService> logger)
        {
            _context = context;
            _auditService = auditService;
            _logger = logger;
        }

        // ── Upload ────────────────────────────────────────────────────────────

        public async Task UploadAsync(
            string userId, IFormFile file, CancellationToken cancellationToken = default)
        {
            if (file is null || file.Length == 0)
            {
                throw new ValidationException("No file was uploaded.");
            }

            // Before decoding, before buffering, before anything. See MaxUploadBytes.
            if (file.Length > MaxUploadBytes)
            {
                throw new ValidationException(
                    $"That image is too large. The limit is {MaxUploadBytes / (1024 * 1024)} MB.");
            }

            var userExists = await _context.Users
                .AnyAsync(u => u.Id == userId, cancellationToken);

            if (!userExists)
            {
                throw new NotFoundException("User", userId);
            }

            // Buffered because the pipeline reads the stream twice — once to identify, once to
            // decode — and a request body stream is forward-only. Bounded by the length check
            // above, so this cannot be used to make the server allocate arbitrarily.
            using var source = new MemoryStream();
            await file.CopyToAsync(source, cancellationToken);
            source.Position = 0;

            var (bytes, width, height) = Reencode(source);

            var etag = ComputeETag(bytes);

            var existing = await _context.UserAvatars
                .FirstOrDefaultAsync(a => a.UserId == userId, cancellationToken);

            if (existing is null)
            {
                existing = new UserAvatar { UserId = userId };
                _context.UserAvatars.Add(existing);
            }

            existing.Bytes = bytes;
            existing.ContentType = "image/webp";
            existing.Width = width;
            existing.Height = height;
            existing.SizeBytes = bytes.Length;
            existing.ETag = etag;
            existing.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            // Audited for the same reason a name change is: this is how a person appears next
            // to their kudos, in the presence list and on the admin roster. It is legitimate
            // self-service, and it should still be traceable if something inappropriate
            // appears platform-wide.
            await _auditService.LogAsync(
                "Account.AvatarUpdated",
                userId,
                $"Stored {bytes.Length} bytes at {width}×{height}.");

            _logger.LogInformation(
                "User {UserId} updated their avatar ({Bytes} bytes).", userId, bytes.Length);
        }

        /// <summary>
        /// Turns whatever was uploaded into a 256×256 WebP the server itself produced.
        ///
        /// ── Why re-encoding is the security control ──────────────────────────
        ///
        /// Nothing above this point can establish that a file is safe. The declared
        /// <c>Content-Type</c> is a string the client chose, and the extension is part of a
        /// filename the client also chose; neither is evidence about the bytes. Even sniffing
        /// the magic number only proves the file *starts* like an image — a polyglot is a file
        /// that is a valid GIF and a valid HTML document at the same time, and it passes every
        /// check of that kind.
        ///
        /// Re-encoding removes the question rather than answering it. What gets stored is a
        /// buffer this process generated from a decoded pixel grid, so any script tag, any PHP
        /// prologue, any SVG <c>onload</c>, any trailing ZIP appended after the image data is
        /// simply not present in the output — it was never part of the pixels. That is why the
        /// uploaded bytes are never stored, not even when the input was already a well-formed
        /// WebP of the right size: "it was already fine" is a judgement, and this pipeline
        /// exists specifically to avoid making judgements about attacker-controlled input.
        /// </summary>
        private static (byte[] Bytes, int Width, int Height) Reencode(MemoryStream source)
        {
            // Identify reads the header only. Doing this before Load is what stops a 3 MB file
            // that claims 30000×30000 from being materialised as pixels first and rejected
            // afterwards — by then the allocation has already happened.
            ImageInfo info;
            try
            {
                info = Image.Identify(source);
            }
            catch (Exception ex) when (ex is ImageFormatException or NotSupportedException)
            {
                throw new ValidationException(
                    "That file could not be read as an image. Try a JPEG, PNG or WebP.");
            }

            if ((long)info.Width * info.Height > MaxPixels)
            {
                throw new ValidationException(
                    "That image has too many pixels. Try a smaller one.");
            }

            source.Position = 0;

            Image image;
            try
            {
                image = Image.Load(source);
            }
            catch (Exception ex) when (ex is ImageFormatException or NotSupportedException)
            {
                // Identify succeeded but the pixel data is truncated or corrupt. Same message:
                // the distinction is not something the person uploading can act on.
                throw new ValidationException(
                    "That file could not be read as an image. Try a JPEG, PNG or WebP.");
            }

            using (image)
            {
                image.Mutate(x => x
                    // AutoOrient must run *before* the metadata is cleared, and before the
                    // resize. A phone photo is often stored landscape with an EXIF tag saying
                    // "rotate this on display"; strip the tag first and the picture is
                    // permanently sideways, because the only record of the rotation is gone.
                    // AutoOrient applies the rotation to the pixels and drops the tag, which
                    // is exactly what a stored, metadata-free image needs.
                    .AutoOrient()

                    // Crop, not Pad or Stretch. A non-square photo has to lose something, and
                    // losing the edges of a portrait is far better than the alternatives:
                    // stretching distorts the face, and padding leaves bars that read as a
                    // rendering bug in a square frame with a hard border.
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(OutputSize, OutputSize),
                        Mode = ResizeMode.Crop,
                        Position = AnchorPositionMode.Center
                    }));

                // ── Strip everything that is not a pixel ─────────────────────
                //
                // The reason is not file size — these profiles are a rounding error next to
                // the image data. It is that a photo taken on a phone carries EXIF GPS
                // coordinates of wherever it was taken, which for a profile picture is very
                // often someone's home. This avatar is served to every signed-in user on the
                // platform. Publishing a scholar's home coordinates because they uploaded a
                // selfie is the actual risk being closed here.
                //
                // Cleared explicitly because Mutate transforms pixels and carries the metadata
                // through untouched — resizing an image does not discard its EXIF.
                image.Metadata.ExifProfile = null;
                image.Metadata.IptcProfile = null;
                image.Metadata.XmpProfile = null;
                image.Metadata.IccProfile = null;

                using var output = new MemoryStream();
                image.Save(output, Encoder);

                return (output.ToArray(), image.Width, image.Height);
            }
        }

        /// <summary>
        /// A short, stable fingerprint of the stored bytes.
        ///
        /// Truncated to 16 bytes of SHA-256. This is a cache key, not a signature — nothing
        /// makes a security decision based on it — and 128 bits is far past the point where an
        /// accidental collision between two avatars is worth thinking about. The full 64
        /// characters would just make every response header longer for no benefit.
        /// </summary>
        private static string ComputeETag(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 16)).ToLowerInvariant();

        // ── Read ──────────────────────────────────────────────────────────────

        public async Task<AvatarImageDto?> GetAsync(
            string userId, CancellationToken cancellationToken = default) =>
            await _context.UserAvatars
                .AsNoTracking()
                .Where(a => a.UserId == userId)

                // Projected rather than returning the entity, so EF does not attach a User
                // navigation this caller has no use for.
                .Select(a => new AvatarImageDto
                {
                    Bytes = a.Bytes,
                    ContentType = a.ContentType,
                    ETag = a.ETag,
                    UpdatedAt = a.UpdatedAt
                })
                .FirstOrDefaultAsync(cancellationToken);

        // ── Delete ────────────────────────────────────────────────────────────

        public async Task DeleteAsync(string userId, CancellationToken cancellationToken = default)
        {
            var existing = await _context.UserAvatars
                .FirstOrDefaultAsync(a => a.UserId == userId, cancellationToken);

            // Already gone. The caller asked for a state, not for an event.
            if (existing is null) return;

            _context.UserAvatars.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync("Account.AvatarRemoved", userId, "Avatar deleted.");

            _logger.LogInformation("User {UserId} removed their avatar.", userId);
        }
    }
}
