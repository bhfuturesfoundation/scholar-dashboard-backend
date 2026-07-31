namespace Auth.Services.Services.News
{
    /// <summary>
    /// ══════════════════════════════════════════════════════════════════════════
    ///  EVERY CSS SELECTOR THAT DEPENDS ON THE SHAPE OF SOMEONE ELSE'S WEBSITE
    /// ══════════════════════════════════════════════════════════════════════════
    ///
    /// <b>If the news widget has gone empty, this is almost certainly the file to fix, and it
    /// should be the only one.</b>
    ///
    /// The selectors below describe <c>https://www.bhfuturesfoundation.org/news</c> as it was
    /// built in July 2026. We do not control that page. It is a Squarespace blog, which means
    /// a redesign is a theme change somebody makes in an afternoon without telling us, and
    /// every class name here can change in that afternoon. That is not a risk to be
    /// engineered away — it is the deal you accept when you scrape — so the design goal is
    /// narrower and achievable: <b>make the breakage obvious, and make the repair a one-file
    /// change.</b>
    ///
    /// ── How to fix a break ───────────────────────────────────────────────────
    ///
    /// 1. Open the news page and View Source. Do not use the browser inspector alone: the
    ///    inspector shows the DOM after JavaScript has run, and this scraper reads the raw
    ///    HTML the server sent. They differ (see the image note below for a case where it
    ///    matters).
    /// 2. Find the element that repeats once per article and update <see cref="PostContainer"/>.
    /// 3. Work down the rest of the list. The log line from <c>NewsPageParser</c> names the
    ///    selector that matched nothing, so you usually only need to change one.
    /// 4. Update <c>Auth.Tests/Fixtures/NewsPageFixture.cs</c> with a fresh copy of the markup
    ///    so the tests describe the site as it is now rather than as it was.
    ///
    /// ── Why these particular selectors ───────────────────────────────────────
    ///
    /// Where there was a choice, the more semantic option won over the prettier one. The
    /// article elements also carry ids like <c>post-6a69d82e88602e30f3931c09</c> and classes
    /// like <c>article-index-1</c>; both are tempting and both are worse, because the first
    /// is per-post and the second encodes a position that shifts every time something is
    /// published.
    /// </summary>
    internal static class NewsPageSelectors
    {
        /// <summary>
        /// The element that repeats once per article.
        ///
        /// <c>article.BlogList-item</c> rather than bare <c>article</c>: the page may grow
        /// other article elements (a featured banner, a related-content block), and the class
        /// is what says "this one is a row in the list".
        /// </summary>
        public const string PostContainer = "article.BlogList-item";

        /// <summary>
        /// The title anchor. Carries both the text and the link, which is why there is no
        /// separate link selector — on this page they are the same element, and treating them
        /// as one thing removes a way for them to disagree.
        ///
        /// The href is site-relative (<c>/news/2026/7/29/...</c>) and is resolved against the
        /// page URL by the parser.
        /// </summary>
        public const string Title = "a.BlogList-item-title";

        /// <summary>
        /// The excerpt paragraph.
        ///
        /// Deliberately <c>.BlogList-item-excerpt p</c> and not <c>.BlogList-item-excerpt</c>.
        /// The container also holds the "Read More" anchor, so taking its text content yields
        /// "…community leadership. Read More" — a trailing call-to-action pasted onto the end
        /// of every excerpt, which reads as our bug on a dashboard card that has no such link.
        /// </summary>
        public const string Excerpt = ".BlogList-item-excerpt p";

        /// <summary>Author display name. A link to that author's archive, so its text is the name.</summary>
        public const string Author = ".Blog-meta-item--author";

        /// <summary>
        /// The publication date element. Read for its <see cref="DateAttribute"/>, not its text.
        /// </summary>
        public const string Date = "time.Blog-meta-item--date";

        /// <summary>
        /// ISO-8601 date, e.g. <c>2026-07-29</c>. Preferred over the element's rendered text
        /// ("July 29, 2026") because the text is only parseable if you assume US English, and
        /// the attribute stays machine-readable if the site is ever localised.
        /// </summary>
        public const string DateAttribute = "datetime";

        /// <summary>The thumbnail image element inside the post's image block.</summary>
        public const string Image = ".BlogList-item-image img";

        /// <summary>
        /// Attributes to try, in order, when looking for the thumbnail's URL.
        ///
        /// <b>This ordering is the single most surprising thing about this page and the reason
        /// a naïve <c>img[src]</c> scraper returns nothing.</b> Squarespace lazy-loads images:
        /// in the served HTML the <c>&lt;img&gt;</c> has <c>data-src</c> and <c>data-image</c>
        /// but <b>no <c>src</c> attribute at all</b> — the real <c>src</c> is written by
        /// Squarespace's own JavaScript once the element scrolls into view. A scraper reads
        /// the served HTML and runs no JavaScript, so <c>src</c> is simply absent for every
        /// post on the page.
        ///
        /// <c>src</c> is still last in the list rather than omitted, because it costs nothing
        /// and it is what the page would use if Squarespace ever turns lazy-loading off.
        /// </summary>
        public static readonly string[] ImageUrlAttributes = { "data-src", "data-image", "src" };

        /// <summary>
        /// The page being read. Also the base for resolving the relative hrefs above, so the
        /// two can never drift apart.
        /// </summary>
        public const string NewsPageUrl = "https://www.bhfuturesfoundation.org/news";
    }
}
