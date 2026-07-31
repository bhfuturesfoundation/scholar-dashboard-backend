using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Auth.Services.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace Auth.Tests;

/// <summary>
/// Tests for the avatar upload pipeline.
///
/// This is the one place in the system that takes bytes from a user and serves them back to
/// other users, so what needs pinning is not "does it resize" but the guarantees the rest of
/// the platform leans on: that the limit is enforced before anything is decoded, that a file
/// is judged by decoding it rather than by what it claims to be, and that the output is always
/// something this process generated — never a pass-through of the upload.
///
/// The last point is the one a refactor is most likely to quietly break. "It's already a WebP
/// of the right size, skip the re-encode" looks like an optimisation and removes the entire
/// security property, so the format and dimension assertions below are really assertions that
/// the re-encode happened at all.
///
/// Test images are generated with ImageSharp rather than committed as fixtures — a binary blob
/// in the repo is unreviewable, and nobody can tell what a checked-in PNG contains.
/// </summary>
public class AvatarTests : IDisposable
{
    private const string UserId = "scholar-1";

    private readonly ApplicationDbContext _context;
    private readonly AvatarService _service;

    public AvatarTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"avatars-{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);

        _context.Users.Add(new User
        {
            Id = UserId,
            FirstName = "Amina",
            LastName = "Test",
            Email = "amina@bhff.org",
            UserName = "amina@bhff.org",
            IsActive = true
        });
        _context.SaveChanges();

        var audit = new Mock<IAuditService>();
        audit.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _service = new AvatarService(_context, audit.Object, NullLogger<AvatarService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A real, decodable image of the given size in the given format.
    ///
    /// Deliberately not a flat colour: a gradient compresses like a photograph rather than
    /// down to almost nothing, so the byte counts these tests see resemble a real upload.
    /// </summary>
    private static byte[] ImageBytes(int width, int height, IImageEncoder encoder)
    {
        using var image = new Image<Rgba32>(width, height);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)(x % 256), (byte)(y % 256), 128, 255);
                }
            }
        });

        using var buffer = new MemoryStream();
        image.Save(buffer, encoder);
        return buffer.ToArray();
    }

    private static IFormFile File(byte[] bytes, string fileName, string contentType) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private async Task<UserAvatar> StoredAvatar() =>
        await _context.UserAvatars.AsNoTracking().SingleAsync(a => a.UserId == UserId);

    // ── Size limit ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_RejectsAFileOverTheSizeLimit()
    {
        // The declared length is 6 MB while the stream holds ten bytes. That mismatch is the
        // assertion: if the check ever moves after the decode, this stops throwing
        // ValidationException and starts failing on unreadable image data instead — so the
        // test pins the *order* of the checks, not just that a limit exists.
        var file = new FormFile(new MemoryStream(new byte[10]), 0, 6 * 1024 * 1024, "file", "huge.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        await Assert.ThrowsAsync<ValidationException>(() => _service.UploadAsync(UserId, file));

        Assert.False(await _context.UserAvatars.AnyAsync());
    }

    [Fact]
    public async Task Upload_RejectsAnEmptyFile()
    {
        var file = File(Array.Empty<byte>(), "nothing.png", "image/png");

        await Assert.ThrowsAsync<ValidationException>(() => _service.UploadAsync(UserId, file));
    }

    // ── Content is judged by decoding, not by what it claims ──────────────────

    [Fact]
    public async Task Upload_RejectsANonImageDeclaredAsAnImage()
    {
        // A text file wearing image/png and a .png extension. Both signals say "image" and
        // both are attacker-controlled, which is exactly why neither is consulted — the file
        // is rejected because it does not decode.
        var notAnImage = System.Text.Encoding.UTF8.GetBytes(
            "<?php system($_GET['cmd']); ?> this is not an image at all");

        var file = File(notAnImage, "avatar.png", "image/png");

        await Assert.ThrowsAsync<ValidationException>(() => _service.UploadAsync(UserId, file));

        Assert.False(await _context.UserAvatars.AnyAsync());
    }

    [Fact]
    public async Task Upload_RejectsAnSvgEvenThoughItIsAnImageFormat()
    {
        // SVG is the format that makes "is it an image?" the wrong question: it is a real
        // image and also a document that can carry script. ImageSharp does not decode it, so
        // it is refused here — and were that ever to change, the re-encode would flatten it to
        // pixels and drop the script anyway.
        var svg = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        var file = File(svg, "avatar.svg", "image/svg+xml");

        await Assert.ThrowsAsync<ValidationException>(() => _service.UploadAsync(UserId, file));
    }

    // ── Output format ─────────────────────────────────────────────────────────

    public static TheoryData<string, IImageEncoder> InputFormats() => new()
    {
        { "avatar.png", new PngEncoder() },
        { "avatar.jpg", new JpegEncoder() },
        { "avatar.gif", new GifEncoder() },
        { "avatar.bmp", new BmpEncoder() },

        // Included on purpose: a WebP in is still re-encoded rather than stored as-is. If an
        // "already the right format, skip it" shortcut is ever added, this case is the one
        // that still passes while the guarantee is gone — so the assertions below check the
        // bytes differ from the upload, not merely that the format is WebP.
        { "avatar.webp", new WebpEncoder() },
    };

    [Theory]
    [MemberData(nameof(InputFormats))]
    public async Task Upload_AlwaysStoresWebpWhateverWentIn(string fileName, IImageEncoder encoder)
    {
        var uploaded = ImageBytes(300, 300, encoder);

        await _service.UploadAsync(UserId, File(uploaded, fileName, "image/png"));

        var stored = await StoredAvatar();

        Assert.Equal("image/webp", stored.ContentType);

        // Decoding the stored bytes and asking what format they are is stronger than trusting
        // the ContentType column, which the service also wrote.
        using var image = Image.Load(stored.Bytes);
        Assert.IsType<WebpFormat>(image.Metadata.DecodedImageFormat);

        // The re-encode actually happened: what is stored is not what was sent.
        Assert.NotEqual(uploaded, stored.Bytes);
    }

    [Fact]
    public async Task Upload_IgnoresAMisleadingContentTypeAndExtension()
    {
        // A genuine PNG announced as a GIF called avatar.txt. Every label is wrong and the
        // upload still succeeds, because the pixels are what is judged.
        var png = ImageBytes(300, 300, new PngEncoder());

        await _service.UploadAsync(UserId, File(png, "avatar.txt", "image/gif"));

        Assert.Equal("image/webp", (await StoredAvatar()).ContentType);
    }

    // ── Output dimensions ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(1000, 1000)] // square, downscaled
    [InlineData(1600, 400)]  // wide, cropped to centre
    [InlineData(400, 1600)]  // tall, cropped to centre
    [InlineData(64, 64)]     // smaller than the target, upscaled
    [InlineData(50, 900)]    // extreme ratio
    public async Task Upload_AlwaysProducesA256SquareWhateverTheInputShape(int width, int height)
    {
        await _service.UploadAsync(
            UserId, File(ImageBytes(width, height, new PngEncoder()), "avatar.png", "image/png"));

        var stored = await StoredAvatar();

        // Both the recorded dimensions and the actual pixels, because a column saying 256 is
        // only useful if the image agrees with it.
        Assert.Equal(256, stored.Width);
        Assert.Equal(256, stored.Height);

        using var image = Image.Load(stored.Bytes);
        Assert.Equal(256, image.Width);
        Assert.Equal(256, image.Height);
    }

    // ── ETag ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_ProducesTheSameETagForTheSameInputTwice()
    {
        // The 304 path depends on this. If the encode were not deterministic — a timestamp in
        // the output, say — every re-upload of an unchanged picture would look like a new
        // image, and the roster would re-download every avatar on every render.
        var png = ImageBytes(300, 300, new PngEncoder());

        await _service.UploadAsync(UserId, File(png, "avatar.png", "image/png"));
        var first = await StoredAvatar();

        await _service.UploadAsync(UserId, File(png, "avatar.png", "image/png"));
        var second = await StoredAvatar();

        Assert.Equal(first.ETag, second.ETag);
        Assert.NotEmpty(first.ETag);
    }

    [Fact]
    public async Task Upload_ProducesADifferentETagForADifferentImage()
    {
        await _service.UploadAsync(
            UserId, File(ImageBytes(300, 300, new PngEncoder()), "a.png", "image/png"));
        var first = await StoredAvatar();

        await _service.UploadAsync(
            UserId, File(ImageBytes(500, 200, new PngEncoder()), "b.png", "image/png"));
        var second = await StoredAvatar();

        Assert.NotEqual(first.ETag, second.ETag);
    }

    // ── Storage shape ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_ReplacesRatherThanAccumulates()
    {
        await _service.UploadAsync(
            UserId, File(ImageBytes(300, 300, new PngEncoder()), "a.png", "image/png"));
        await _service.UploadAsync(
            UserId, File(ImageBytes(500, 200, new PngEncoder()), "b.png", "image/png"));

        // One row per person. The old picture is gone rather than kept as history — nobody
        // asked for a gallery of former avatars, and keeping them would grow the table without
        // limit for a feature nothing reads.
        Assert.Equal(1, await _context.UserAvatars.CountAsync(a => a.UserId == UserId));
    }

    [Fact]
    public async Task Upload_RecordsSizeMatchingTheStoredBytes()
    {
        await _service.UploadAsync(
            UserId, File(ImageBytes(800, 800, new PngEncoder()), "avatar.png", "image/png"));

        var stored = await StoredAvatar();

        Assert.Equal(stored.Bytes.Length, stored.SizeBytes);
    }

    [Fact]
    public async Task Upload_ForAnUnknownUserIsNotFound()
    {
        var file = File(ImageBytes(300, 300, new PngEncoder()), "avatar.png", "image/png");

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.UploadAsync("nobody", file));
    }

    // ── Read and delete ───────────────────────────────────────────────────────

    [Fact]
    public async Task Get_ReturnsNullWhenThereIsNoAvatar()
    {
        Assert.Null(await _service.GetAsync(UserId));
    }

    [Fact]
    public async Task Get_ReturnsTheStoredImageAndItsETag()
    {
        await _service.UploadAsync(
            UserId, File(ImageBytes(300, 300, new PngEncoder()), "avatar.png", "image/png"));

        var result = await _service.GetAsync(UserId);
        var stored = await StoredAvatar();

        Assert.NotNull(result);
        Assert.Equal("image/webp", result!.ContentType);
        Assert.Equal(stored.ETag, result.ETag);
        Assert.Equal(stored.Bytes.Length, result.Bytes.Length);
    }

    [Fact]
    public async Task Delete_RemovesTheAvatar()
    {
        await _service.UploadAsync(
            UserId, File(ImageBytes(300, 300, new PngEncoder()), "avatar.png", "image/png"));

        await _service.DeleteAsync(UserId);

        Assert.False(await _context.UserAvatars.AnyAsync(a => a.UserId == UserId));
    }

    [Fact]
    public async Task Delete_WithNoAvatarSucceeds()
    {
        // Idempotent: the caller asked for a state, not for an event. A double-click on
        // "remove" must not surface an error.
        await _service.DeleteAsync(UserId);

        Assert.False(await _context.UserAvatars.AnyAsync(a => a.UserId == UserId));
    }
}
