namespace Auth.Models.Entities.News
{
    /// <summary>
    /// One article from the foundation's public news page, mirrored into our database.
    ///
    /// ── Why mirror at all ────────────────────────────────────────────────────
    ///
    /// This replaces a hardcoded TypeScript array. Every news item used to need a code
    /// change and a deploy, which is why the list on the dashboard was months out of date:
    /// the cost of updating it was a pull request, so nobody paid it. The public site is
    /// already the place the marketing team publishes to, so making it the source of truth
    /// removes the second place that had to be kept in sync.
    ///
    /// Mirrored rather than proxied on demand because the dashboard must not depend on a
    /// third-party site being up during a page load. The scrape happens once a day in the
    /// background; the widget only ever reads our own table.
    ///
    /// ── Why the image bytes live here ────────────────────────────────────────
    ///
    /// The obvious alternative is to store the remote CDN URL and let the browser fetch it.
    /// That was rejected for three reasons, in increasing order of how annoying they are to
    /// debug:
    ///
    /// 1. Hotlinking breaks the moment the site reorganises. Squarespace URLs embed a content
    ///    hash and a folder id; a re-upload of the same picture changes them.
    /// 2. Some hosts block cross-origin image loads outright, so the thumbnail would work in
    ///    development and silently fail from the deployed origin.
    /// 3. A dead thumbnail is worse than no thumbnail. A missing image renders as a broken
    ///    icon in a layout that expected a picture, which looks like our bug, not theirs.
    ///
    /// Storing the bytes means the widget's image is served from an origin we control, with
    /// an ETag we computed, and it keeps working even after the source article is deleted.
    /// The volume argument is the same one <c>UserAvatar</c> makes: three posts at 640px WebP
    /// is a few hundred kilobytes for the whole table.
    /// </summary>
    public class NewsPost
    {
        public int Id { get; set; }

        /// <summary>
        /// Absolute URL of the article on the public site. The natural key.
        ///
        /// Deduping on this rather than on the title is deliberate: titles get edited after
        /// publication (typos, capitalisation, a subtitle added), and every edit would create
        /// a duplicate row if the title were the key. The URL slug is generated once when the
        /// post is created and does not change afterwards, so it is the only field on the page
        /// that actually identifies an article.
        /// </summary>
        public string SourceUrl { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The site's own summary paragraph. May legitimately be empty — a post without an
        /// excerpt is unusual but valid — so an empty value here is NOT treated as a parse
        /// failure, unlike an empty title or URL.
        /// </summary>
        public string Excerpt { get; set; } = string.Empty;

        public string Author { get; set; } = string.Empty;

        /// <summary>
        /// Publication date, taken from the <c>datetime</c> attribute of the page's
        /// <c>&lt;time&gt;</c> element rather than from its display text.
        ///
        /// The attribute is ISO-8601 and locale-independent; the text is "July 29, 2026",
        /// which is only parseable if you already know the site renders US English. If the
        /// site ever adds localisation, the text changes and the attribute does not.
        /// </summary>
        public DateTime PublishedAt { get; set; }

        // ── Thumbnail ─────────────────────────────────────────────────────────

        /// <summary>
        /// The re-encoded thumbnail, or null when there was no image or the download failed.
        ///
        /// Nullable on purpose, and the API exposes it as a <c>hasImage</c> flag: the widget
        /// then decides between an image and a text-only card up front, instead of rendering
        /// an <c>&lt;img&gt;</c> that might 404.
        /// </summary>
        public byte[]? ImageBytes { get; set; }

        /// <summary>Always "image/webp" for rows written today. See <c>NewsScraperService</c>.</summary>
        public string? ImageContentType { get; set; }

        /// <summary>
        /// Short hash of <see cref="ImageBytes"/>, used as the HTTP entity tag.
        ///
        /// Stored rather than computed per request, for the same reason <c>UserAvatar</c>
        /// stores it: answering <c>If-None-Match</c> with a 304 should not require loading the
        /// bytes we have just decided not to send.
        /// </summary>
        public string? ImageETag { get; set; }

        // ── Bookkeeping ───────────────────────────────────────────────────────

        /// <summary>
        /// When this row was last confirmed against the public site.
        ///
        /// This is the schedule. <c>NewsScraperBackgroundService</c> asks "what is the newest
        /// FetchedAt in the table" to decide whether a scrape is due, so the cadence survives
        /// a redeploy — see that class for why an in-memory timer would not.
        /// </summary>
        public DateTime FetchedAt { get; set; }

        /// <summary>
        /// Position on the source page, 0 first.
        ///
        /// Kept because the site's own ordering carries information that the dates do not:
        /// two posts published on the same day have identical <see cref="PublishedAt"/>
        /// values, and the editorial order between them is only visible in the page. Reads
        /// order by date first and this second, so same-day posts keep the order the site
        /// chose rather than whichever one Postgres happened to return.
        /// </summary>
        public int SortOrder { get; set; }
    }
}
