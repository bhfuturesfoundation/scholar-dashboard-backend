using System.Security.Cryptography;
using Auth.Models.Data;
using Auth.Models.DTOs.News;
using Auth.Models.Entities.News;
using Auth.Services.Interfaces.News;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Auth.Services.Services.News
{
    /// <inheritdoc cref="INewsScraperService"/>
    public class NewsScraperService : INewsScraperService
    {
        /// <summary>
        /// How many articles are mirrored. Three, because that is what the dashboard widget
        /// shows — there is no reason to store a backlog we would never render, and every
        /// stored post costs a thumbnail download.
        /// </summary>
        public const int MaxPosts = 3;

        /// <summary>
        /// Hard cap on a downloaded thumbnail, enforced while reading rather than after.
        ///
        /// The size is not the interesting part; *when* it is checked is. A
        /// <c>Content-Length</c> header is a claim made by somebody else's server and can be
        /// absent or wrong, so this counts the bytes as they arrive and abandons the read the
        /// moment it goes over. Trusting the header would leave a server free to stream until
        /// the container runs out of memory.
        /// </summary>
        private const long MaxImageBytes = 5 * 1024 * 1024;

        /// <summary>
        /// Widest the stored thumbnail may be. The widget renders these in a card roughly
        /// 300px across, so 640 is comfortably sharp on a 2× display and still an order of
        /// magnitude smaller than the 1280px originals the source CDN serves.
        /// </summary>
        private const int MaxImageWidth = 640;

        /// <summary>
        /// Decompression-bomb guard, read from the image header before any pixels are
        /// allocated. Same reasoning as <c>AvatarService</c>: compression ratio is unbounded,
        /// so a small valid PNG can decode to hundreds of megabytes, and the byte cap above
        /// does not constrain that at all.
        /// </summary>
        private const long MaxImagePixels = 40_000_000;

        /// <summary>
        /// Quality 80 is the usual WebP sweet spot — visually indistinguishable from 100 at
        /// this size, and it lands a 640px thumbnail around 30–60 KB.
        /// </summary>
        private static readonly WebpEncoder Encoder = new()
        {
            Quality = 80,
            FileFormat = WebpFileFormatType.Lossy
        };

        /// <summary>
        /// The named <c>HttpClient</c> registered in <c>Program.cs</c>, which carries the
        /// timeout and the User-Agent.
        /// </summary>
        public const string HttpClientName = "NewsScraper";

        private readonly ApplicationDbContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<NewsScraperService> _logger;

        public NewsScraperService(
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            ILogger<NewsScraperService> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        // ── Read ──────────────────────────────────────────────────────────────

        public async Task<NewsFeedDto> GetFeedAsync(int limit, CancellationToken cancellationToken = default)
        {
            if (limit <= 0) limit = MaxPosts;

            var posts = await _context.NewsPosts
                .AsNoTracking()

                // Date first, page order second. Two posts published on the same day carry
                // identical dates — three of the current five do — and SortOrder is the only
                // record of which one the site listed first.
                .OrderByDescending(p => p.PublishedAt)
                .ThenBy(p => p.SortOrder)
                .Take(limit)

                // Projected, never the entity. NewsPost carries the image bytes, and
                // materialising the entity would drag every thumbnail across the wire on a
                // request whose entire job is to return a few hundred bytes of text.
                .Select(p => new NewsPostDto
                {
                    Id = p.Id,
                    Title = p.Title,
                    Excerpt = p.Excerpt,
                    Author = p.Author,
                    PublishedAt = p.PublishedAt,
                    SourceUrl = p.SourceUrl,
                    HasImage = p.ImageBytes != null,
                    ImageETag = p.ImageETag
                })
                .ToListAsync(cancellationToken);

            return new NewsFeedDto
            {
                Posts = posts,
                LastFetchedAt = await GetLastFetchedAtAsync(cancellationToken)
            };
        }

        public async Task<NewsImageDto?> GetImageAsync(int id, CancellationToken cancellationToken = default) =>
            await _context.NewsPosts
                .AsNoTracking()
                .Where(p => p.Id == id && p.ImageBytes != null)
                .Select(p => new NewsImageDto
                {
                    Bytes = p.ImageBytes!,
                    ContentType = p.ImageContentType ?? "image/webp",
                    ETag = p.ImageETag ?? string.Empty
                })
                .FirstOrDefaultAsync(cancellationToken);

        public async Task<DateTime?> GetLastFetchedAtAsync(CancellationToken cancellationToken = default) =>
            await _context.NewsPosts
                .AsNoTracking()

                // Max over a nullable projection rather than MaxAsync directly: MaxAsync on an
                // empty table throws InvalidOperationException, and "nothing has been scraped
                // yet" is a normal state on a fresh database, not an error.
                .Select(p => (DateTime?)p.FetchedAt)
                .MaxAsync(cancellationToken);

        // ── Refresh ───────────────────────────────────────────────────────────

        public async Task<NewsScrapeResultDto> RefreshAsync(CancellationToken cancellationToken = default)
        {
            // ── Nothing below this line may throw ─────────────────────────────
            //
            // The outer try is the backstop for everything the inner Try-shaped helpers do
            // not already cover — a DbUpdateException on save, an ImageSharp failure in a
            // codec path, an OutOfMemory on a hostile file. See INewsScraperService for why
            // an escaping exception here would be a permanently dead scheduler.
            try
            {
                var (html, fetchError) = await FetchPageAsync(cancellationToken);

                if (html is null)
                {
                    // The site is down, slow, or refusing us. The stored rows are not touched:
                    // yesterday's news is still true, and blanking the widget because a CDN
                    // had a bad minute would be strictly worse than showing it.
                    _logger.LogWarning("News scrape skipped: {Error}", fetchError);
                    return Failure(fetchError!);
                }

                var parsed = NewsPageParser.Parse(html, NewsPageSelectors.NewsPageUrl, MaxPosts);

                if (!parsed.Success)
                {
                    // Loud, because this one does not fix itself. A network blip is expected
                    // and recovers on the next run; a parse failure means the page was
                    // redesigned and will keep failing every day until somebody edits
                    // NewsPageSelectors. LogError so it surfaces wherever errors surface,
                    // rather than joining the warnings nobody reads.
                    _logger.LogError(
                        "News scrape could not read the page: {Error} Existing posts were left " +
                        "untouched.", parsed.Error);

                    return Failure(parsed.Error!, parsed.Warnings);
                }

                var result = await ApplyAsync(parsed.Posts, cancellationToken);
                result.Warnings.InsertRange(0, parsed.Warnings);

                _logger.LogInformation(
                    "News scrape complete: {Added} added, {Updated} updated, {Removed} removed, " +
                    "{Images} image(s) stored.",
                    result.Added, result.Updated, result.Removed, result.ImagesStored);

                foreach (var warning in result.Warnings)
                {
                    _logger.LogWarning("News scrape: {Warning}", warning);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "News scrape failed unexpectedly. Existing posts were left untouched.");
                return Failure(ex.Message);
            }
        }

        /// <summary>
        /// Writes the drafts to the database.
        ///
        /// Internal rather than private so the tests can exercise the upsert without a network
        /// stack — <c>Auth.Services</c> already grants <c>InternalsVisibleTo</c> to Auth.Tests
        /// for exactly this kind of seam.
        /// </summary>
        internal async Task<NewsScrapeResultDto> ApplyAsync(
            IReadOnlyList<NewsPostDraft> drafts, CancellationToken cancellationToken)
        {
            var result = new NewsScrapeResultDto { Success = true };
            var now = DateTime.UtcNow;

            var urls = drafts.Select(d => d.SourceUrl).ToList();

            // One query for the whole batch rather than a lookup per draft. Three round trips
            // is not a performance problem at this size — it is a habit worth not forming.
            var existing = await _context.NewsPosts
                .Where(p => urls.Contains(p.SourceUrl))
                .ToListAsync(cancellationToken);

            var byUrl = existing.ToDictionary(p => p.SourceUrl, StringComparer.Ordinal);

            foreach (var draft in drafts)
            {
                if (!byUrl.TryGetValue(draft.SourceUrl, out var row))
                {
                    row = new NewsPost { SourceUrl = draft.SourceUrl };
                    _context.NewsPosts.Add(row);
                    result.Added++;
                }
                else if (HasChanged(row, draft))
                {
                    // Counted only on a real change, so the normal "nothing was published
                    // today" run reports 0/0 rather than 3 updates that changed nothing. An
                    // operator reading the refresh result should be able to tell those apart.
                    result.Updated++;
                }

                row.Title = draft.Title;
                row.Excerpt = draft.Excerpt;
                row.Author = draft.Author;
                row.PublishedAt = draft.PublishedAt;
                row.SortOrder = draft.SortOrder;
                row.FetchedAt = now;

                await ApplyImageAsync(row, draft, result, cancellationToken);
            }

            // ── Pruning ───────────────────────────────────────────────────────
            //
            // Guarded on having received the full expected set. The table is meant to mirror
            // the top of the public page, so rows that fell off it should go — otherwise the
            // table grows forever and the widget's ordering has to work around posts nobody
            // asked it to keep.
            //
            // The guard is the important half. Deleting on a partial parse would mean a run
            // that managed to read one post out of three wipes the other two, turning a small
            // markup change into data loss. Full count or nothing.
            if (drafts.Count == MaxPosts)
            {
                var stale = await _context.NewsPosts
                    .Where(p => !urls.Contains(p.SourceUrl))
                    .ToListAsync(cancellationToken);

                if (stale.Count > 0)
                {
                    _context.NewsPosts.RemoveRange(stale);
                    result.Removed = stale.Count;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            result.CompletedAt = now;
            return result;
        }

        private static bool HasChanged(NewsPost row, NewsPostDraft draft) =>
            row.Title != draft.Title ||
            row.Excerpt != draft.Excerpt ||
            row.Author != draft.Author ||
            row.PublishedAt != draft.PublishedAt ||
            row.SortOrder != draft.SortOrder;

        /// <summary>
        /// Downloads and stores the thumbnail, leaving any existing image in place if this
        /// attempt does not produce a better one.
        /// </summary>
        private async Task ApplyImageAsync(
            NewsPost row, NewsPostDraft draft, NewsScrapeResultDto result,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(draft.ImageUrl)) return;

            var image = await TryFetchImageAsync(draft.ImageUrl, cancellationToken);

            if (image is null)
            {
                // ── Why this does not clear the existing bytes ────────────────
                //
                // The download is retried on every daily run, which is what makes a transient
                // CDN failure self-healing. That only works if a failure is a no-op: if this
                // assigned null, one bad minute at the CDN would replace a perfectly good
                // stored thumbnail with nothing, and the card would lose its picture until
                // the image happened to change upstream.
                result.Warnings.Add($"Could not store the image for '{row.Title}'.");
                return;
            }

            row.ImageBytes = image.Value.Bytes;
            row.ImageContentType = "image/webp";
            row.ImageETag = image.Value.ETag;
            result.ImagesStored++;
        }

        // ── HTTP ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches the news page. Returns the HTML, or null and a reason. Never throws.
        /// </summary>
        private async Task<(string? Html, string? Error)> FetchPageAsync(CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);

                using var response = await client.GetAsync(
                    NewsPageSelectors.NewsPageUrl,

                    // Headers first, so the status code and content type can be judged before
                    // the body is pulled down.
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return (null, $"The news page returned HTTP {(int)response.StatusCode}.");
                }

                var bytes = await ReadBoundedAsync(response.Content, MaxPageBytes, cancellationToken);

                if (bytes is null)
                {
                    return (null, $"The news page exceeded the {MaxPageBytes / 1024 / 1024} MB read limit.");
                }

                return (System.Text.Encoding.UTF8.GetString(bytes), null);
            }
            catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // We are shutting down, not failing. Distinguished from the timeout below,
                // which surfaces as the same exception type with a different trigger.
                return (null, "The scrape was cancelled.");
            }
            catch (TaskCanceledException)
            {
                return (null, "The news page timed out.");
            }
            catch (Exception ex)
            {
                return (null, $"The news page could not be fetched: {ex.Message}");
            }
        }

        /// <summary>
        /// Generous, because it is a backstop and not a budget. The real page is ~170 KB; this
        /// only exists so a server that streams forever cannot exhaust the container.
        /// </summary>
        private const long MaxPageBytes = 10 * 1024 * 1024;

        /// <summary>
        /// Downloads one thumbnail and re-encodes it. Returns null on any failure — a missing
        /// picture is never worth losing the news over.
        /// </summary>
        private async Task<(byte[] Bytes, string ETag)?> TryFetchImageAsync(
            string url, CancellationToken cancellationToken)
        {
            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);

                using var response = await client.GetAsync(
                    url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Thumbnail {Url} returned HTTP {Status}.", url, (int)response.StatusCode);
                    return null;
                }

                var bytes = await ReadBoundedAsync(response.Content, MaxImageBytes, cancellationToken);

                if (bytes is null)
                {
                    _logger.LogWarning(
                        "Thumbnail {Url} exceeded the {Limit} MB cap.", url, MaxImageBytes / 1024 / 1024);
                    return null;
                }

                return Reencode(bytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail {Url} could not be downloaded.", url);
                return null;
            }
        }

        /// <summary>
        /// Reads a response body, giving up if it goes past <paramref name="max"/>.
        ///
        /// Returns null rather than throwing on overrun, because "too big" is an expected
        /// outcome here rather than an exceptional one.
        /// </summary>
        private static async Task<byte[]?> ReadBoundedAsync(
            HttpContent content, long max, CancellationToken cancellationToken)
        {
            // A declared length over the cap is refused before a single byte is transferred.
            // The header cannot be trusted to be present or honest, which is why the counted
            // read below still exists — but when it is there, it saves the download.
            if (content.Headers.ContentLength is > 0 and var declared && declared > max)
            {
                return null;
            }

            await using var stream = await content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();

            var chunk = new byte[81920];
            int read;

            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > max) return null;
                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        // ── Image processing ──────────────────────────────────────────────────

        /// <summary>
        /// Turns a downloaded thumbnail into a bounded WebP this server produced.
        ///
        /// Re-encoding rather than storing the fetched bytes is the same control
        /// <c>AvatarService</c> applies to uploads, and it is worth applying to a third party's
        /// bytes for the same reason: what comes back from a URL found in someone else's HTML
        /// is not established to be an image by the fact that it was linked as one. Decoding
        /// to a pixel grid and re-encoding means what we store and later serve under
        /// <c>image/webp</c> is a buffer this process generated, not a file an attacker chose.
        ///
        /// It also happens to be why the thumbnails are small.
        /// </summary>
        private static (byte[] Bytes, string ETag)? Reencode(byte[] source)
        {
            using var input = new MemoryStream(source);

            // Header only. Doing this before Load is what stops a small file claiming
            // 30000×30000 from being materialised as pixels and rejected afterwards.
            ImageInfo info;
            try
            {
                info = Image.Identify(input);
            }
            catch (Exception ex) when (ex is ImageFormatException or NotSupportedException)
            {
                return null;
            }

            if ((long)info.Width * info.Height > MaxImagePixels) return null;

            input.Position = 0;

            Image image;
            try
            {
                image = Image.Load(input);
            }
            catch (Exception ex) when (ex is ImageFormatException or NotSupportedException)
            {
                return null;
            }

            using (image)
            {
                // Downscale only. The guard matters: without it a source thumbnail narrower
                // than 640px would be blown up to 640, which costs bytes and looks worse than
                // the original.
                if (image.Width > MaxImageWidth)
                {
                    // Height 0 tells ImageSharp to preserve the aspect ratio. These are news
                    // photographs in assorted shapes, so cropping them to a fixed box — which
                    // is right for a square avatar — would cut the tops off people's heads.
                    image.Mutate(x => x.Resize(MaxImageWidth, 0));
                }

                // Stripped for the same reason as avatars: a photograph taken on a phone
                // carries EXIF GPS coordinates. These pictures are already public, so this is
                // tidiness rather than a fix — but re-publishing someone else's metadata from
                // our own domain is not something to do by accident.
                image.Metadata.ExifProfile = null;
                image.Metadata.IptcProfile = null;
                image.Metadata.XmpProfile = null;
                image.Metadata.IccProfile = null;

                using var output = new MemoryStream();
                image.Save(output, Encoder);

                var bytes = output.ToArray();
                return (bytes, ComputeETag(bytes));
            }
        }

        /// <summary>
        /// Short, stable fingerprint of the stored bytes. A cache key, not a signature —
        /// 128 bits of SHA-256 is far past the point where an accidental collision between
        /// two thumbnails is worth thinking about.
        /// </summary>
        private static string ComputeETag(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 16)).ToLowerInvariant();

        private static NewsScrapeResultDto Failure(string error, List<string>? warnings = null) =>
            new()
            {
                Success = false,
                Error = error,
                Warnings = warnings ?? new List<string>()
            };
    }
}
