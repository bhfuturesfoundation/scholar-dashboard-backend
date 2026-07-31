using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace Auth.Services.Services.News
{
    /// <summary>One article as read off the page, before anything has touched the database.</summary>
    internal sealed class NewsPostDraft
    {
        public string SourceUrl { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Excerpt { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public DateTime PublishedAt { get; init; }

        /// <summary>Absolute URL of the thumbnail on the source CDN, or null if there was none.</summary>
        public string? ImageUrl { get; init; }

        public int SortOrder { get; init; }
    }

    /// <summary>The outcome of reading one page.</summary>
    internal sealed class NewsParseResult
    {
        public bool Success => Error is null;

        public IReadOnlyList<NewsPostDraft> Posts { get; init; } = Array.Empty<NewsPostDraft>();

        /// <summary>Set when the page could not be read at all. Null on success.</summary>
        public string? Error { get; init; }

        /// <summary>Survivable oddities — a post skipped, an author missing.</summary>
        public List<string> Warnings { get; init; } = new();

        public static NewsParseResult Fail(string error) => new() { Error = error };
    }

    /// <summary>
    /// Turns the public news page's HTML into drafts.
    ///
    /// ── The one rule this class exists to enforce ────────────────────────────
    ///
    /// <b>A post is either complete or it does not exist.</b> Every field that the widget
    /// actually renders — title, link, date — is required, and a post missing any of them is
    /// dropped and reported rather than stored with an empty string in it.
    ///
    /// That rule is the whole design. The failure mode this guards against is not "the parser
    /// crashes" — a crash is loud and gets fixed. It is the quiet one: the site is
    /// redesigned, every selector matches nothing, <c>?.TextContent ?? ""</c> dutifully
    /// returns empty strings, and the scraper cheerfully overwrites three good rows with
    /// three blank cards. Nothing throws, nothing logs, and the widget shows three empty
    /// boxes until somebody happens to look at it. Requiring the fields up front converts
    /// that silent corruption into a hard failure that leaves the previous rows untouched.
    ///
    /// ── Pure by design ───────────────────────────────────────────────────────
    ///
    /// Takes an HTML string and returns a result. No HTTP, no database, no clock beyond the
    /// dates on the page. That is what lets the tests run the real parser against a saved
    /// fixture: a test that reaches the live site fails on a train, fails in CI behind a
    /// proxy, and starts failing for real the day the foundation publishes something new.
    ///
    /// No regular expressions are used to find elements. HTML is not a regular language and
    /// the failure is never a clean one — it is a selector that silently matches the wrong
    /// half of a nested tag. AngleSharp builds the same DOM a browser would.
    /// </summary>
    internal static class NewsPageParser
    {
        /// <summary>
        /// Collapses runs of whitespace. The markup is pretty-printed, so text content arrives
        /// full of newlines and indentation that would otherwise be stored verbatim and show
        /// up as odd gaps in the rendered card.
        /// </summary>
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        public static NewsParseResult Parse(string html, string pageUrl, int maxPosts)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return NewsParseResult.Fail("The news page returned an empty body.");
            }

            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri))
            {
                return NewsParseResult.Fail($"'{pageUrl}' is not a valid absolute URL.");
            }

            var document = new HtmlParser().ParseDocument(html);

            var containers = document.QuerySelectorAll(NewsPageSelectors.PostContainer);

            // Nothing matched. Either the page was redesigned or we were served something
            // that is not the news page at all — a login wall, a Cloudflare interstitial, an
            // error page under HTTP 200. All of those look identical from here, so the
            // message names the selector and points at the file to fix.
            if (containers.Length == 0)
            {
                return NewsParseResult.Fail(
                    $"No articles matched '{NewsPageSelectors.PostContainer}'. The page layout has " +
                    "probably changed — see NewsPageSelectors.");
            }

            var posts = new List<NewsPostDraft>();
            var warnings = new List<string>();

            foreach (var container in containers.Take(maxPosts))
            {
                var index = posts.Count;

                // ── Title and link ────────────────────────────────────────────
                // One element carries both on this page, which is deliberate: it removes any
                // way for the text and the href to come from different articles.
                var titleElement = container.QuerySelector(NewsPageSelectors.Title);

                var title = Clean(titleElement?.TextContent);
                var href = titleElement?.GetAttribute("href");

                if (string.IsNullOrEmpty(title))
                {
                    warnings.Add(
                        $"Skipped article #{index + 1}: no text in '{NewsPageSelectors.Title}'.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(href))
                {
                    warnings.Add($"Skipped '{Trim(title)}': the title element had no href.");
                    continue;
                }

                // Hrefs on this page are site-relative ("/news/2026/7/29/..."). Resolved
                // against the page URL rather than string-concatenated onto a hardcoded host,
                // so an absolute href would also work if the site ever emits one.
                if (!Uri.TryCreate(baseUri, href, out var absolute))
                {
                    warnings.Add($"Skipped '{Trim(title)}': '{href}' is not a resolvable URL.");
                    continue;
                }

                // ── Date ──────────────────────────────────────────────────────
                var dateElement = container.QuerySelector(NewsPageSelectors.Date);

                if (!TryReadDate(dateElement?.GetAttribute(NewsPageSelectors.DateAttribute),
                                 dateElement?.TextContent,
                                 out var publishedAt))
                {
                    // Required, and this is the reason why. The card shows the date, so a post
                    // stored without one either renders blank or — worse, if we defaulted to
                    // "now" — stamps today's date on an article from last year. A wrong date
                    // is a lie the reader has no way to detect; a skipped post is merely a
                    // gap somebody notices and reports.
                    warnings.Add(
                        $"Skipped '{Trim(title)}': no usable date in " +
                        $"'{NewsPageSelectors.Date}' [{NewsPageSelectors.DateAttribute}].");
                    continue;
                }

                // ── Optional fields ───────────────────────────────────────────
                // Genuinely optional: a post with no excerpt or no author byline is unusual
                // but valid, and dropping it over that would lose real news.
                var excerpt = Clean(container.QuerySelector(NewsPageSelectors.Excerpt)?.TextContent);
                var author = Clean(container.QuerySelector(NewsPageSelectors.Author)?.TextContent);

                if (string.IsNullOrEmpty(author))
                {
                    warnings.Add($"'{Trim(title)}' has no author.");
                }

                posts.Add(new NewsPostDraft
                {
                    SourceUrl = absolute.ToString(),
                    Title = title,
                    Excerpt = excerpt,
                    Author = author,
                    PublishedAt = publishedAt,
                    ImageUrl = ReadImageUrl(container, baseUri),
                    SortOrder = index
                });
            }

            // Articles were found but not one of them yielded a usable post. This is the
            // redesign case where the container class happened to survive and everything
            // inside it changed — exactly the situation that would otherwise write three
            // blank rows over three good ones.
            if (posts.Count == 0)
            {
                return NewsParseResult.Fail(
                    $"Matched {containers.Length} article(s) but none had a usable title, link and " +
                    $"date. The page layout has probably changed — see NewsPageSelectors. " +
                    $"Details: {string.Join(" ", warnings)}");
            }

            return new NewsParseResult { Posts = posts, Warnings = warnings };
        }

        /// <summary>
        /// The thumbnail URL, or null.
        ///
        /// Returns null rather than failing: a post without a picture is a text card, which
        /// the widget already handles. Losing the news over a missing image would be a bad
        /// trade.
        /// </summary>
        private static string? ReadImageUrl(AngleSharp.Dom.IElement container, Uri baseUri)
        {
            var image = container.QuerySelector(NewsPageSelectors.Image);
            if (image is null) return null;

            // In order, because the first hit wins. See NewsPageSelectors.ImageUrlAttributes
            // for why 'src' is not the first one — on this page it is not there at all.
            foreach (var attribute in NewsPageSelectors.ImageUrlAttributes)
            {
                var value = image.GetAttribute(attribute);
                if (string.IsNullOrWhiteSpace(value)) continue;

                if (Uri.TryCreate(baseUri, value, out var absolute) &&
                    (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
                {
                    // http/https only. A lazy-loading theme often puts a base64 'data:' URI in
                    // one of these attributes as the placeholder blur, and downloading that
                    // would store a 20-pixel smudge as the thumbnail.
                    return absolute.ToString();
                }
            }

            return null;
        }

        /// <summary>
        /// Reads the publication date, preferring the machine-readable attribute.
        ///
        /// The attribute is ISO-8601 ("2026-07-29"). The text is "July 29, 2026", which is
        /// only parseable if you already assume the site renders US English — so it is the
        /// fallback, not the source, and it is parsed with an explicit invariant culture
        /// rather than the thread's. On a container with a different locale, <c>Parse</c>
        /// with the ambient culture would read the same string differently or not at all,
        /// which is the kind of bug that only appears in production.
        /// </summary>
        private static bool TryReadDate(string? attribute, string? text, out DateTime published)
        {
            if (!string.IsNullOrWhiteSpace(attribute) &&
                DateTime.TryParse(attribute, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out published))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(text) &&
                DateTime.TryParse(Clean(text), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out published))
            {
                return true;
            }

            published = default;
            return false;
        }

        private static string Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : Whitespace.Replace(value, " ").Trim();

        /// <summary>Keeps a long headline from dominating a log line.</summary>
        private static string Trim(string value) =>
            value.Length <= 60 ? value : value[..60] + "…";
    }
}
