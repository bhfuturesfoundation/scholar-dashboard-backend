using Auth.Services.Interfaces.News;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.News
{
    /// <summary>
    /// Re-scrapes the foundation's public news page once a day.
    ///
    /// ── Why the schedule lives in the database ───────────────────────────────
    ///
    /// Built on "has a scrape happened today", read from the persisted <c>FetchedAt</c>
    /// column, rather than on a <c>PeriodicTimer</c> ticking every 24 hours. This is the same
    /// reasoning as <c>ScheduledBackupService</c>, and it matters more here, not less:
    ///
    /// A 24-hour in-memory timer only fires if the process stays up for 24 hours. This app
    /// deploys often — several times on an active day — and every deploy restarts the
    /// container and resets the timer to zero. A daily timer in a service that redeploys
    /// twice a day does not fire late; it fires <b>never</b>. The news would have been
    /// stale in a different, more confusing way than the hardcoded array it replaced.
    ///
    /// Deriving it from stored state fixes all of that at once. The schedule survives
    /// restarts; an instance that was down at 05:00 catches up when it comes back; and
    /// several instances converge safely, because whichever one wakes first writes
    /// <c>FetchedAt</c> and the others see it and skip.
    /// </summary>
    public class NewsScraperBackgroundService : BackgroundService
    {
        /// <summary>
        /// How often "is a scrape due" is evaluated. Hourly is far more often than the daily
        /// schedule fires, but it is one indexed aggregate over a three-row table, and it
        /// means an instance that was down at the scheduled hour catches up within the hour
        /// instead of waiting a full day.
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<NewsScraperBackgroundService> _logger;

        public NewsScraperBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<NewsScraperBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        private bool Enabled =>
            !string.Equals(_configuration["NEWS_SCRAPE_ENABLED"]?.Trim(), "false",
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Hour (UTC) the daily scrape runs. Default 05:00 — before the working day in
        /// Sarajevo (07:00 local), so the dashboard is already current when anyone opens it,
        /// and far from the 02:00 backup window so the two jobs are not competing.
        /// </summary>
        private int ScheduledHourUtc =>
            int.TryParse(_configuration["NEWS_SCRAPE_HOUR_UTC"], out var hour) && hour is >= 0 and <= 23
                ? hour
                : 5;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Enabled)
            {
                _logger.LogInformation("The news scraper is disabled (NEWS_SCRAPE_ENABLED=false).");
                return;
            }

            _logger.LogInformation(
                "News scraper active: daily at {Hour:00}:00 UTC.", ScheduledHourUtc);

            // Let migrations and seeding finish before touching the database. The NewsPosts
            // table may not exist yet on the deploy that introduces it, and a scrape racing
            // its own migration would fail for a reason that has nothing to do with the news.
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A BackgroundService that throws is torn down permanently and silently.
                    // RefreshAsync is already Try-shaped and should never get here, so this is
                    // the backstop for the scope and the query around it.
                    _logger.LogError(ex, "News scraper tick failed. Continuing.");
                }

                try { await Task.Delay(PollInterval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task TickAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var scraper = scope.ServiceProvider.GetRequiredService<INewsScraperService>();

            var now = DateTime.UtcNow;
            var lastFetchedAt = await scraper.GetLastFetchedAtAsync(cancellationToken);

            if (!IsDue(lastFetchedAt, now)) return;

            _logger.LogInformation(
                "News scrape starting (last fetched {Last}).",
                lastFetchedAt?.ToString("u") ?? "never");

            // Not wrapped in a try. RefreshAsync does not throw by contract, and it has
            // already logged whatever went wrong in the detail an operator needs. The result
            // is inspected rather than discarded so the outcome appears in the log even on the
            // days when nothing changed.
            var result = await scraper.RefreshAsync(cancellationToken);

            if (!result.Success)
            {
                // Deliberately does not retry before the next poll. If the site is down, it
                // will still be down in a minute, and hammering a third party's server because
                // our scrape failed is how a scraper gets blocked. The hourly poll is the
                // retry.
                _logger.LogWarning(
                    "News scrape did not complete: {Error} The previous posts are still being served.",
                    result.Error);
            }
        }

        /// <summary>
        /// Whether a scrape should happen now.
        ///
        /// Two ways to be due, and the first is not just a convenience:
        ///
        /// <b>Never scraped.</b> An empty table means the widget has nothing at all to show,
        /// which is the one state worse than stale news. Waiting until 05:00 to fix that would
        /// leave a freshly migrated deployment with a blank widget for up to a day. So the
        /// first run ignores the hour entirely.
        ///
        /// <b>Scraped, but not today.</b> The normal path. Comparing dates rather than
        /// measuring a 24-hour gap keeps the job anchored to a wall-clock hour instead of
        /// drifting later every day by however long the previous run took.
        /// </summary>
        private bool IsDue(DateTime? lastFetchedAt, DateTime now)
        {
            if (lastFetchedAt is null) return true;

            return now.Hour >= ScheduledHourUtc && lastFetchedAt.Value.Date < now.Date;
        }
    }
}
