namespace Auth.Models.DTOs.News
{
    /// <summary>
    /// One news card as the dashboard widget needs it.
    ///
    /// Note what is absent: the image bytes. They are served by their own endpoint so that
    /// listing the news is a small JSON response rather than a few hundred kilobytes of
    /// base64 the browser cannot cache separately from the text.
    /// </summary>
    public class NewsPostDto
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
        public string Excerpt { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }

        /// <summary>Absolute link to the article on the public site — the card opens this.</summary>
        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>
        /// Whether <c>/api/news/{id}/image</c> will return an image for this post.
        ///
        /// A flag rather than a nullable URL, because the client needs to decide between two
        /// layouts *before* it renders. Handing it a URL that might 404 is what produces the
        /// broken-image icon this design is trying to avoid.
        /// </summary>
        public bool HasImage { get; set; }

        /// <summary>
        /// The stored image's entity tag, used by the client as a cache-buster in the query
        /// string. Without it a thumbnail that changes upstream would keep showing the old
        /// bytes for as long as the browser's cache entry lives.
        /// </summary>
        public string? ImageETag { get; set; }
    }

    /// <summary>The stored thumbnail, as the image endpoint needs it.</summary>
    public class NewsImageDto
    {
        public byte[] Bytes { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = string.Empty;
        public string ETag { get; set; } = string.Empty;
    }

    /// <summary>
    /// The widget's payload: the posts plus when they were last confirmed.
    ///
    /// <see cref="LastFetchedAt"/> is here because "the news is old" and "the scraper is
    /// broken" look identical from the client otherwise. A quiet fortnight on the public site
    /// and a parser that has silently failed for a fortnight produce exactly the same list.
    /// </summary>
    public class NewsFeedDto
    {
        public List<NewsPostDto> Posts { get; set; } = new();

        /// <summary>Null when nothing has ever been scraped — a fresh database.</summary>
        public DateTime? LastFetchedAt { get; set; }
    }

    /// <summary>
    /// What a scrape did, returned by the manual refresh endpoint so an operator gets an
    /// answer rather than a spinner that stops.
    ///
    /// Deliberately a result object rather than an exception on failure. This is called both
    /// by a person and by a background service; a hosted service that lets an exception
    /// escape is torn down permanently and silently, so "failed" has to be a value the caller
    /// can inspect, not control flow. See <c>NewsScraperService</c>.
    /// </summary>
    public class NewsScrapeResultDto
    {
        public bool Success { get; set; }

        public int Added { get; set; }
        public int Updated { get; set; }
        public int Removed { get; set; }

        /// <summary>How many thumbnails were downloaded and stored on this run.</summary>
        public int ImagesStored { get; set; }

        /// <summary>
        /// Why it failed, in language an operator can act on — "the page loaded but no
        /// articles matched the selector" rather than "sequence contains no elements".
        /// Null on success.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Non-fatal oddities: a post missing an author, an image that would not download.
        /// Surfaced rather than swallowed, because a slow drift in the site's markup shows up
        /// here first — one warning a run for a month before the selector breaks entirely.
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    }
}
