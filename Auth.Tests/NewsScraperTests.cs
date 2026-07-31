using Auth.Models.Data;
using Auth.Models.Entities.News;
using Auth.Services.Services.News;
using Auth.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Auth.Tests;

/// <summary>
/// Tests for the news scraper: the parser against saved markup, and the upsert against an
/// in-memory database.
///
/// ── What these are actually protecting ───────────────────────────────────────
///
/// This code replaced a hardcoded array. The array was stale and English-only, but it had one
/// virtue worth preserving: it could not spontaneously become empty. A scraper can, and it can
/// do it silently — someone redesigns a website we do not control, every selector stops
/// matching, and a parser built on <c>?.TextContent ?? string.Empty</c> writes three blank rows
/// over three good ones without raising anything.
///
/// So the assertions come in two halves. The happy path pins that the right values come out of
/// real markup. The rest pin the failure behaviour, which is the half that matters: a page the
/// parser cannot read must produce a loud, specific error and leave the database alone.
///
/// Nothing here touches the network. See <see cref="NewsPageFixture"/> for why that is a
/// deliberate limit rather than an oversight.
/// </summary>
public class NewsScraperTests : IDisposable
{
    private readonly ApplicationDbContext _context;

    public NewsScraperTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"news-{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
    }

    public void Dispose() => _context.Dispose();

    private const string PageUrl = "https://www.bhfuturesfoundation.org/news";

    /// <summary>
    /// The service under test with an <c>IHttpClientFactory</c> that is never called.
    ///
    /// Strict, so a change that starts making HTTP calls from the upsert path fails loudly
    /// here rather than quietly reaching out to a real CDN from a unit test.
    /// </summary>
    private NewsScraperService BuildService() => new(
        _context,
        new Mock<IHttpClientFactory>(MockBehavior.Strict).Object,
        NullLogger<NewsScraperService>.Instance);

    private static NewsPostDraft Draft(
        string url, string title = "A title", string excerpt = "An excerpt",
        string author = "Marketing BHFF", int sortOrder = 0, DateTime? publishedAt = null) =>
        new()
        {
            SourceUrl = url,
            Title = title,
            Excerpt = excerpt,
            Author = author,
            PublishedAt = publishedAt ?? new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc),
            ImageUrl = null,          // no network from a unit test
            SortOrder = sortOrder
        };

    // ══════════════════════════════════════════════════════════════════════════
    //  Parsing the real page
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_ExtractsTheThreeNewestPosts()
    {
        var result = NewsPageParser.Parse(NewsPageFixture.ThreeNewestPosts, PageUrl, 3);

        Assert.True(result.Success, result.Error);
        Assert.Equal(3, result.Posts.Count);
    }

    [Fact]
    public void Parse_ReadsEveryFieldOffTheFirstPost()
    {
        var post = NewsPageParser.Parse(NewsPageFixture.ThreeNewestPosts, PageUrl, 3).Posts[0];

        Assert.Equal(
            "BHFF Alumni Melisa Musić and Dino Burić Lead MedTech Workshop at Futures Academy " +
            "in Zenica, Showcasing the Power of Mentorship and Innovation",
            post.Title);

        Assert.Equal("Marketing BHFF", post.Author);
        Assert.Equal(new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc), post.PublishedAt);
        Assert.StartsWith("At Futures Academy in Zenica, BHFF alumni delivered", post.Excerpt);
        Assert.Equal(0, post.SortOrder);
    }

    [Fact]
    public void Parse_KeepsThePageOrderRatherThanReorderingByDate()
    {
        var posts = NewsPageParser.Parse(NewsPageFixture.ThreeNewestPosts, PageUrl, 3).Posts;

        // The second and third posts share a publication date (27 July). Sorting by date
        // alone would make their relative order arbitrary, which is exactly why SortOrder
        // exists and is stored.
        Assert.Equal("BHFF Sends Its Scholars to the Global Stage: Berlin’s WeAreDevelopers Conference",
            posts[1].Title);
        Assert.Equal("A New Generation Joins the BH Futures Alumni Community", posts[2].Title);

        Assert.Equal(posts[1].PublishedAt, posts[2].PublishedAt);
        Assert.Equal(new[] { 0, 1, 2 }, posts.Select(p => p.SortOrder).ToArray());
    }

    [Fact]
    public void Parse_ResolvesRelativeLinksAgainstThePageUrl()
    {
        var post = NewsPageParser.Parse(NewsPageFixture.ThreeNewestPosts, PageUrl, 3).Posts[0];

        // The href on the page is site-relative. Storing it verbatim would give the widget a
        // link that resolves against the *dashboard's* origin and 404s.
        Assert.StartsWith("https://www.bhfuturesfoundation.org/news/2026/7/29/", post.SourceUrl);
    }

    [Fact]
    public void Parse_FindsTheImageInDataSrcRatherThanSrc()
    {
        var post = NewsPageParser.Parse(NewsPageFixture.ThreeNewestPosts, PageUrl, 3).Posts[0];

        // The single most load-bearing assertion in this file. Squarespace lazy-loads, so the
        // served markup has data-src and data-image and NO src attribute at all — the src is
        // written later by their JavaScript, which a scraper never runs. A parser that looks
        // for img[src] finds nothing and every post silently loses its thumbnail.
        Assert.NotNull(post.ImageUrl);
        Assert.StartsWith("https://images.squarespace-cdn.com/", post.ImageUrl);
    }

    [Fact]
    public void Parse_ExcludesTheReadMoreLinkFromTheExcerpt()
    {
        var post = NewsPageParser.Parse(NewsPageFixture.ThreeNewestPosts, PageUrl, 3).Posts[0];

        // The excerpt container also holds the "Read More" anchor, so selecting the container
        // instead of its paragraph pastes a call-to-action onto the end of every excerpt —
        // on a card that has no such link.
        Assert.DoesNotContain("Read More", post.Excerpt);
        Assert.EndsWith("community leadership.", post.Excerpt);
    }

    [Fact]
    public void Parse_CollapsesThePrettyPrintedWhitespace()
    {
        var post = NewsPageParser.Parse(NewsPageFixture.ThreeNewestPosts, PageUrl, 3).Posts[0];

        Assert.DoesNotContain("\n", post.Title);
        Assert.DoesNotContain("  ", post.Excerpt);
        Assert.Equal(post.Title.Trim(), post.Title);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Failure behaviour when the site changes
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Parse_FailsLoudlyWhenTheContainerSelectorMatchesNothing()
    {
        var result = NewsPageParser.Parse(NewsPageFixture.RedesignedContainer, PageUrl, 3);

        Assert.False(result.Success);
        Assert.Empty(result.Posts);

        // The message has to name the selector and point at the file. Six months from now the
        // person reading this log line will not have this code in their head.
        Assert.Contains("article.BlogList-item", result.Error);
        Assert.Contains("NewsPageSelectors", result.Error);
    }

    [Fact]
    public void Parse_FailsRatherThanReturningThreeEmptyPostsWhenTheInnerMarkupChanges()
    {
        // The whole point of the exercise. Three containers ARE found here, so a parser that
        // only checked "did I find any articles" would sail straight through and hand back
        // three posts with empty titles and no dates.
        var result = NewsPageParser.Parse(NewsPageFixture.RedesignedInnerMarkup, PageUrl, 3);

        Assert.False(result.Success);
        Assert.Empty(result.Posts);
        Assert.Contains("NewsPageSelectors", result.Error);
    }

    [Fact]
    public void Parse_NeverProducesAPostWithAnEmptyTitleOrUrl()
    {
        // Stated as its own invariant across every fixture, because it is the property the
        // whole design exists to guarantee — not an incidental consequence of the cases above.
        var fixtures = new[]
        {
            NewsPageFixture.ThreeNewestPosts,
            NewsPageFixture.RedesignedInnerMarkup,
            NewsPageFixture.RedesignedContainer,
            NewsPageFixture.OneGoodOneBroken
        };

        foreach (var html in fixtures)
        {
            foreach (var post in NewsPageParser.Parse(html, PageUrl, 3).Posts)
            {
                Assert.False(string.IsNullOrWhiteSpace(post.Title));
                Assert.False(string.IsNullOrWhiteSpace(post.SourceUrl));
                Assert.NotEqual(default, post.PublishedAt);
            }
        }
    }

    [Fact]
    public void Parse_KeepsTheGoodPostWhenOnlyOneArticleIsMalformed()
    {
        var result = NewsPageParser.Parse(NewsPageFixture.OneGoodOneBroken, PageUrl, 3);

        // Partial, not fatal. One badly formed article is not evidence of a redesign, and
        // discarding a real post over its neighbour would be an overreaction.
        Assert.True(result.Success, result.Error);
        Assert.Single(result.Posts);
        Assert.Equal("A perfectly good post", result.Posts[0].Title);

        // Reported, though — silent partial success is how a slow drift in the markup goes
        // unnoticed until it becomes a total failure.
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Parse_FailsOnAnEmptyBody()
    {
        var result = NewsPageParser.Parse(string.Empty, PageUrl, 3);

        Assert.False(result.Success);
        Assert.Empty(result.Posts);
    }

    [Fact]
    public void Parse_HonoursTheRequestedPostCount()
    {
        Assert.Single(NewsPageParser.Parse(NewsPageFixture.ThreeNewestPosts, PageUrl, 1).Posts);
        Assert.Equal(2, NewsPageParser.Parse(NewsPageFixture.ThreeNewestPosts, PageUrl, 2).Posts.Count);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Upsert
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Apply_InsertsPostsOnAnEmptyTable()
    {
        var result = await BuildService().ApplyAsync(
            new[] { Draft("https://x/a"), Draft("https://x/b", sortOrder: 1) }, default);

        Assert.True(result.Success);
        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(2, await _context.NewsPosts.CountAsync());
    }

    [Fact]
    public async Task Apply_UpdatesTheExistingRowRatherThanInsertingADuplicate()
    {
        const string url = "https://www.bhfuturesfoundation.org/news/2026/7/29/a-post";

        _context.NewsPosts.Add(new NewsPost
        {
            SourceUrl = url,
            Title = "The original headline",
            Excerpt = "The original excerpt",
            Author = "Marketing BHFF",
            PublishedAt = new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc),
            FetchedAt = new DateTime(2026, 7, 30, 5, 0, 0, DateTimeKind.Utc)
        });
        await _context.SaveChangesAsync();

        var result = await BuildService().ApplyAsync(
            new[] { Draft(url, title: "The corrected headline", excerpt: "A rewritten excerpt") },
            default);

        // Editing a published post is routine — a typo fixed, a subtitle added. Keying on the
        // URL is what makes that an update. Keying on the title, which is the other obvious
        // choice, would insert a second row for every such edit and the widget would show the
        // same story twice.
        Assert.Equal(1, await _context.NewsPosts.CountAsync());
        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Updated);

        var stored = await _context.NewsPosts.SingleAsync();
        Assert.Equal("The corrected headline", stored.Title);
        Assert.Equal("A rewritten excerpt", stored.Excerpt);
    }

    [Fact]
    public async Task Apply_ReportsNoUpdateWhenNothingActuallyChanged()
    {
        var draft = Draft("https://x/a");

        var service = BuildService();
        await service.ApplyAsync(new[] { draft }, default);
        var second = await service.ApplyAsync(new[] { draft }, default);

        // The normal case: the site published nothing today. An operator who clicks refresh
        // should see "nothing changed" rather than a fictional update count.
        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Updated);
        Assert.Equal(1, await _context.NewsPosts.CountAsync());
    }

    [Fact]
    public async Task Apply_StampsFetchedAtSoTheSchedulerCanSeeTheRunHappened()
    {
        var before = DateTime.UtcNow;
        var service = BuildService();

        await service.ApplyAsync(new[] { Draft("https://x/a") }, default);

        var lastFetchedAt = await service.GetLastFetchedAtAsync();

        // This column IS the schedule — NewsScraperBackgroundService reads it to decide
        // whether a run is due. A scrape that forgot to stamp it would re-run every hour.
        Assert.NotNull(lastFetchedAt);
        Assert.InRange(lastFetchedAt!.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task GetLastFetchedAt_IsNullOnAFreshDatabase()
    {
        // "Never scraped" is a normal state, not an error — MaxAsync over a non-nullable
        // projection would throw here instead, and the scheduler would crash on first boot.
        Assert.Null(await BuildService().GetLastFetchedAtAsync());
    }

    [Fact]
    public async Task Apply_PrunesPostsThatFellOffThePage()
    {
        var service = BuildService();

        await service.ApplyAsync(new[]
        {
            Draft("https://x/old-1", sortOrder: 0),
            Draft("https://x/old-2", sortOrder: 1),
            Draft("https://x/old-3", sortOrder: 2)
        }, default);

        var result = await service.ApplyAsync(new[]
        {
            Draft("https://x/new", sortOrder: 0),
            Draft("https://x/old-1", sortOrder: 1),
            Draft("https://x/old-2", sortOrder: 2)
        }, default);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Removed);
        Assert.Equal(3, await _context.NewsPosts.CountAsync());
        Assert.False(await _context.NewsPosts.AnyAsync(p => p.SourceUrl == "https://x/old-3"));
    }

    [Fact]
    public async Task Apply_DoesNotPruneOnAPartialParse()
    {
        var service = BuildService();

        await service.ApplyAsync(new[]
        {
            Draft("https://x/a", sortOrder: 0),
            Draft("https://x/b", sortOrder: 1),
            Draft("https://x/c", sortOrder: 2)
        }, default);

        // A run that could only read one post. Pruning here would delete the other two —
        // turning a small markup change into data loss, which is the failure this guard
        // exists to prevent.
        var result = await service.ApplyAsync(new[] { Draft("https://x/a") }, default);

        Assert.Equal(0, result.Removed);
        Assert.Equal(3, await _context.NewsPosts.CountAsync());
    }

    [Fact]
    public async Task Apply_LeavesAnExistingThumbnailAloneWhenTheDraftHasNoImage()
    {
        const string url = "https://x/a";

        _context.NewsPosts.Add(new NewsPost
        {
            SourceUrl = url,
            Title = "Has a picture",
            ImageBytes = new byte[] { 1, 2, 3 },
            ImageContentType = "image/webp",
            ImageETag = "abc123"
        });
        await _context.SaveChangesAsync();

        await BuildService().ApplyAsync(new[] { Draft(url, title: "Still has a picture") }, default);

        // A failed or absent image download must never blank a thumbnail we already hold.
        // Otherwise one bad minute at the CDN costs the card its picture until the image
        // happens to change upstream.
        var stored = await _context.NewsPosts.SingleAsync();
        Assert.Equal(new byte[] { 1, 2, 3 }, stored.ImageBytes);
        Assert.Equal("abc123", stored.ImageETag);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Reading it back
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetFeed_OrdersByDateThenPageOrder()
    {
        var service = BuildService();
        var july27 = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);
        var july29 = new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc);

        await service.ApplyAsync(new[]
        {
            Draft("https://x/newest", title: "Newest", publishedAt: july29, sortOrder: 0),
            Draft("https://x/same-day-first", title: "Same day, listed first", publishedAt: july27, sortOrder: 1),
            Draft("https://x/same-day-second", title: "Same day, listed second", publishedAt: july27, sortOrder: 2)
        }, default);

        var feed = await service.GetFeedAsync(3);

        Assert.Equal(
            new[] { "Newest", "Same day, listed first", "Same day, listed second" },
            feed.Posts.Select(p => p.Title).ToArray());
    }

    [Fact]
    public async Task GetFeed_ReportsWhetherEachPostHasAnImage()
    {
        _context.NewsPosts.AddRange(
            new NewsPost
            {
                SourceUrl = "https://x/with",
                Title = "With a picture",
                PublishedAt = new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc),
                ImageBytes = new byte[] { 1, 2, 3 },
                ImageETag = "abc123"
            },
            new NewsPost
            {
                SourceUrl = "https://x/without",
                Title = "Without a picture",
                PublishedAt = new DateTime(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc)
            });
        await _context.SaveChangesAsync();

        var feed = await BuildService().GetFeedAsync(10);

        // The flag is what lets the widget choose its layout before rendering, instead of
        // pointing an <img> at a URL that might 404 and showing a broken-image icon.
        Assert.True(feed.Posts[0].HasImage);
        Assert.Equal("abc123", feed.Posts[0].ImageETag);
        Assert.False(feed.Posts[1].HasImage);
    }

    [Fact]
    public async Task GetImage_ReturnsNothingForAPostWithoutOne()
    {
        _context.NewsPosts.Add(new NewsPost { SourceUrl = "https://x/a", Title = "No picture" });
        await _context.SaveChangesAsync();

        var id = (await _context.NewsPosts.SingleAsync()).Id;

        Assert.Null(await BuildService().GetImageAsync(id));
        Assert.Null(await BuildService().GetImageAsync(9999));
    }
}
