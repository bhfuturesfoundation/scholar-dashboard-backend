using Auth.Models.DTOs.News;

namespace Auth.Services.Interfaces.News
{
    /// <summary>
    /// Mirrors the foundation's public news page into our own table, and reads it back.
    ///
    /// ── The contract that matters ────────────────────────────────────────────
    ///
    /// <b><see cref="RefreshAsync"/> does not throw.</b> Not for a network error, not for a
    /// redesigned page, not for a malformed image. It returns a result describing what
    /// happened, in the same <c>Try</c>-shaped spirit as <c>IDropboxStorage</c> and for the
    /// same reason: the callers are a background service and an operations screen. A hosted
    /// service that lets an exception escape is torn down permanently and silently, so the
    /// one thing this must never do is turn a bad afternoon at a third-party website into a
    /// dead scheduler.
    ///
    /// <b>A failed refresh leaves the stored posts exactly as they were.</b> The widget keeps
    /// showing last week's news, which is correct behaviour — stale news is information, an
    /// empty widget is a bug report.
    /// </summary>
    public interface INewsScraperService
    {
        /// <summary>The stored posts, newest first, for the dashboard widget.</summary>
        Task<NewsFeedDto> GetFeedAsync(int limit, CancellationToken cancellationToken = default);

        /// <summary>The stored thumbnail, or null when this post has none.</summary>
        Task<NewsImageDto?> GetImageAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches the public page, parses it and upserts. Never throws — see the type remarks.
        /// </summary>
        Task<NewsScrapeResultDto> RefreshAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// The newest <c>FetchedAt</c> in the table, or null if nothing has ever been scraped.
        ///
        /// This is the persisted schedule <c>NewsScraperBackgroundService</c> reads to decide
        /// whether a run is due. Exposed on the interface rather than left as a private query
        /// so the scheduler does not need its own <c>DbContext</c>.
        /// </summary>
        Task<DateTime?> GetLastFetchedAtAsync(CancellationToken cancellationToken = default);
    }
}
