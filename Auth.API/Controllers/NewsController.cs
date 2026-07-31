using Auth.Models.Constants;
using Auth.Models.DTOs.News;
using Auth.Models.Response;
using Auth.Services.Interfaces.News;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers
{
    /// <summary>
    /// The dashboard's news widget, mirrored from the foundation's public website.
    ///
    /// Reading the list needs a signed-in account; re-scraping on demand is staff-only,
    /// because it reaches out to a third party's server and is the one action here with a
    /// cost outside this process.
    /// </summary>
    [Route("api/news")]
    [ApiController]
    [Authorize]
    public class NewsController : ControllerBase
    {
        private readonly INewsScraperService _news;
        private readonly ILogger<NewsController> _logger;

        public NewsController(INewsScraperService news, ILogger<NewsController> logger)
        {
            _news = news;
            _logger = logger;
        }

        /// <summary>
        /// The stored posts, newest first.
        ///
        /// Reads our own table and never touches the public site, so a slow afternoon at
        /// Squarespace cannot slow down a dashboard load. Freshness is the background
        /// scraper's job.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ApiResponse<NewsFeedDto>>> GetNews(
            [FromQuery] int limit = 3, CancellationToken ct = default) =>
            Ok(ApiResponse<NewsFeedDto>.SuccessResponse(
                await _news.GetFeedAsync(limit, ct), "News retrieved"));

        /// <summary>
        /// Serves a post's stored thumbnail.
        ///
        /// ── Why this one endpoint is anonymous ────────────────────────────────
        ///
        /// Every other endpoint in this controller requires a token, and this one deliberately
        /// does not. Two reasons, and the practical one comes first:
        ///
        /// <b>An &lt;img&gt; tag cannot send an Authorization header.</b> This app keeps its
        /// access token in localStorage, not in a cookie, so the browser has no way to attach
        /// it to an image request. Requiring auth here would force the client to fetch each
        /// thumbnail with JavaScript into a blob and hand the &lt;img&gt; an object URL —
        /// which works, but throws away the entire point of the ETag below, because a blob URL
        /// is opaque to the HTTP cache and the browser can no longer revalidate it.
        ///
        /// <b>There is nothing here to protect.</b> These bytes are a re-encoded copy of a
        /// picture the foundation publishes on its own public website, reachable by anyone
        /// with a browser and no account at all. Guessing an id here reveals a news
        /// thumbnail. Access control exists to protect scholars' data, and spending it on
        /// something already on the open internet buys nothing while costing real caching
        /// behaviour.
        ///
        /// Raw bytes rather than the usual ApiResponse envelope, because the consumer is an
        /// &lt;img src&gt; and the browser needs an image, not JSON with base64 in it.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("{id:int}/image")]
        public async Task<IActionResult> GetImage(int id, CancellationToken ct)
        {
            var image = await _news.GetImageAsync(id, ct);

            // 404 rather than a placeholder. The list already told the client whether a post
            // has an image via HasImage, so a request that lands here for a post without one
            // is a client bug worth surfacing, not a case to paper over.
            if (image is null) return NotFound();

            // Compared before the body is touched — the only ordering that actually saves
            // anything, since checking after loading would still have read every byte. The
            // hash is stored on the row, so this costs one string comparison.
            var requested = Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(image.ETag) &&
                !string.IsNullOrEmpty(requested) &&
                requested.Contains(image.ETag, StringComparison.Ordinal))
            {
                return StatusCode(StatusCodes.Status304NotModified);
            }

            if (!string.IsNullOrEmpty(image.ETag))
            {
                Response.Headers.ETag = $"\"{image.ETag}\"";
            }

            // Public, unlike the avatar endpoint's private — this is not one member's data,
            // so a shared cache in front of the API is welcome to hold it. A day of max-age
            // is safe because the content at a given id only changes when the scraper stores
            // new bytes, and the client appends the ETag as a cache-buster when it does.
            Response.Headers.CacheControl = "public, max-age=86400, must-revalidate";

            return File(image.Bytes, image.ContentType);
        }

        /// <summary>
        /// Re-scrapes the public site now, rather than waiting for the daily run.
        ///
        /// Staff-only because it makes an outbound request to somebody else's server on
        /// demand. Returns what changed rather than a bare 200: an operator who clicks this
        /// wants to know whether it worked, and "0 added, 0 updated" is a real answer that a
        /// success toast alone would hide.
        ///
        /// Note this returns 200 even when the scrape failed — the failure is reported inside
        /// the result. That is not sloppiness: the HTTP request succeeded, and a 500 would
        /// tell the client that this endpoint is broken when what actually happened is that a
        /// third-party website was unreachable. The distinction matters to whoever is on call.
        /// </summary>
        [HttpPost("refresh")]
        [Authorize(Roles = AppRoles.JournalOversight)]
        public async Task<ActionResult<ApiResponse<NewsScrapeResultDto>>> Refresh(CancellationToken ct)
        {
            var result = await _news.RefreshAsync(ct);

            _logger.LogInformation(
                "Manual news refresh by {User}: success={Success}, added={Added}, updated={Updated}.",
                User.Identity?.Name, result.Success, result.Added, result.Updated);

            return Ok(ApiResponse<NewsScrapeResultDto>.SuccessResponse(result, "News refreshed"));
        }
    }
}
