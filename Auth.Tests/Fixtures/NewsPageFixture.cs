namespace Auth.Tests.Fixtures
{
    /// <summary>
    /// A saved copy of https://www.bhfuturesfoundation.org/news, captured 31 July 2026.
    ///
    /// -- Why a fixture and not the live site --------------------------------
    ///
    /// A test that fetches the real page is not a test of our parser. It fails on a train,
    /// it fails in CI behind a proxy, and - worst of the three - it starts failing for real
    /// the day the foundation publishes something new, because the assertions name three
    /// specific headlines that will have moved down the page. That failure looks exactly
    /// like a broken parser, which is how a suite trains everyone to ignore it.
    ///
    /// Frozen markup gives the opposite property: these tests fail only when *our* code
    /// changes behaviour. The trade is that they cannot notice the site being redesigned -
    /// nothing offline can. That is what the loud runtime failure in NewsPageParser is for.
    ///
    /// -- Keeping it honest ---------------------------------------------------
    ///
    /// This is real markup, copied verbatim, not markup written to make the parser pass.
    /// Only the surrounding page chrome (head, nav, footer, the other 17 articles) has been
    /// removed. Every attribute on the elements below is as Squarespace served it, including
    /// the detail that matters most: the &lt;img&gt; tags carry data-src and data-image but
    /// <b>no src at all</b>. Rewriting that to a plain src would make the fixture agree with
    /// an assumption the real page does not share, and the test would pass while production
    /// stored no images at all.
    ///
    /// When the site is redesigned and NewsPageSelectors is updated, re-capture this from
    /// View Source - not from the browser inspector, which shows the DOM after Squarespace's
    /// lazy-loader has written the src attributes that are absent from the wire.
    /// </summary>
    internal static class NewsPageFixture
    {
        /// <summary>The three newest posts exactly as the site served them.</summary>
        public const string ThreeNewestPosts = Prefix + """
        <article id="post-6a69d82e88602e30f3931c09" class="BlogList-item hentry author-marketing-bhff post-type-text article-index-1" data-item-id="6a69d82e88602e30f3931c09">


                <div class="BlogList-item-image">
                  <a href="/news/2026/7/29/bhff-alumni-melisa-musi-and-dino-buri-lead-medtech-workshop-at-futures-academy-in-zenica-showcasing-the-power-of-mentorship-and-innovation" class="BlogList-item-image-link">
                    <img data-src="https://images.squarespace-cdn.com/content/v1/585218fd37c58186144e9933/1785321661256-XM3W1CI1SNN3RPQU21EU/1785267290597.jpg" data-image="https://images.squarespace-cdn.com/content/v1/585218fd37c58186144e9933/1785321661256-XM3W1CI1SNN3RPQU21EU/1785267290597.jpg" data-image-dimensions="1280x970" data-image-focal-point="0.5,0.5" alt="BHFF Alumni Melisa Musić and Dino Burić Lead MedTech Workshop at Futures Academy in Zenica, Showcasing the Power of Mentorship and Innovation"  data-load="false" />
                  </a>
                </div>



              <a href="/news/2026/7/29/bhff-alumni-melisa-musi-and-dino-buri-lead-medtech-workshop-at-futures-academy-in-zenica-showcasing-the-power-of-mentorship-and-innovation" class="BlogList-item-title" data-content-field="title">BHFF Alumni Melisa Musić and Dino Burić Lead MedTech Workshop at Futures Academy in Zenica, Showcasing the Power of Mentorship and Innovation</a>



                  <div class="BlogList-item-excerpt">
                    <p style="white-space:pre-wrap;" data-rte-preserve-empty="true">At Futures Academy in Zenica, BHFF alumni delivered a workshop on medtech, demonstrating how mentorship and early opportunities evolve into impactful knowledge-sharing and community leadership.</p>
                    <a href="/news/2026/7/29/bhff-alumni-melisa-musi-and-dino-buri-lead-medtech-workshop-at-futures-academy-in-zenica-showcasing-the-power-of-mentorship-and-innovation" class="BlogList-item-readmore">
                      <span>Read More</span>
                    </a>
                  </div>



              <div class="Blog-meta BlogList-item-meta">
                <!--

                Author

                --><a href="/news?author=6329c21690377f6f3f80347b" class="Blog-meta-item Blog-meta-item--author">Marketing BHFF</a><!--

                Date

                --><time class="Blog-meta-item Blog-meta-item--date" datetime="2026-07-29">July 29, 2026</time>
              </div>



            </article>
        <article id="post-6a67aaf86870d85b35293032" class="BlogList-item hentry author-melisa-music post-type-text article-index-2" data-item-id="6a67aaf86870d85b35293032">


                <div class="BlogList-item-image">
                  <a href="/news/2026/7/27/bhff-sends-its-scholars-to-the-global-stage-berlins-wearedevelopers-conference" class="BlogList-item-image-link">
                    <img data-src="https://images.squarespace-cdn.com/content/v1/585218fd37c58186144e9933/1785183677041-EI956X5DOGJK9ECF8UGR/IMG_8449.jpg" data-image="https://images.squarespace-cdn.com/content/v1/585218fd37c58186144e9933/1785183677041-EI956X5DOGJK9ECF8UGR/IMG_8449.jpg" data-image-dimensions="4032x3024" data-image-focal-point="0.5,0.5" alt="BHFF Sends Its Scholars to the Global Stage: Berlin’s WeAreDevelopers Conference"  data-load="false" />
                  </a>
                </div>



              <a href="/news/2026/7/27/bhff-sends-its-scholars-to-the-global-stage-berlins-wearedevelopers-conference" class="BlogList-item-title" data-content-field="title">BHFF Sends Its Scholars to the Global Stage: Berlin’s WeAreDevelopers Conference</a>



                  <div class="BlogList-item-excerpt">
                    <p style="white-space:pre-wrap;" data-rte-preserve-empty="true">BHFF empowered its Scholars to attend the WAD 26 - WeAreDevelopers conference in Berlin, gaining global exposure, connecting with leading tech companies, and showcasing the potential of young talent from Bosnia and Herzegovina.</p>
                    <a href="/news/2026/7/27/bhff-sends-its-scholars-to-the-global-stage-berlins-wearedevelopers-conference" class="BlogList-item-readmore">
                      <span>Read More</span>
                    </a>
                  </div>



              <div class="Blog-meta BlogList-item-meta">
                <!--

                Author

                --><a href="/news?author=68305a9f91e11455764ad263" class="Blog-meta-item Blog-meta-item--author">Melisa Music</a><!--

                Date

                --><time class="Blog-meta-item Blog-meta-item--date" datetime="2026-07-27">July 27, 2026</time>
              </div>



            </article>
        <article id="post-6a67668e7c55fb06985d48ca" class="BlogList-item hentry author-marketing-bhff post-type-text article-index-3" data-item-id="6a67668e7c55fb06985d48ca">


                <div class="BlogList-item-image">
                  <a href="/news/2026/7/27/a-new-generation-joins-the-bh-futures-alumni-community" class="BlogList-item-image-link">
                    <img data-src="https://images.squarespace-cdn.com/content/v1/585218fd37c58186144e9933/1785176774360-P88JBACRXC6X62R2HWST/2026_07_25_18_23_IMG_6070.JPG" data-image="https://images.squarespace-cdn.com/content/v1/585218fd37c58186144e9933/1785176774360-P88JBACRXC6X62R2HWST/2026_07_25_18_23_IMG_6070.JPG" data-image-dimensions="4032x3024" data-image-focal-point="0.5,0.5" alt="A New Generation Joins the BH Futures Alumni Community"  data-load="false" />
                  </a>
                </div>



              <a href="/news/2026/7/27/a-new-generation-joins-the-bh-futures-alumni-community" class="BlogList-item-title" data-content-field="title">A New Generation Joins the BH Futures Alumni Community</a>



                  <div class="BlogList-item-excerpt">
                    <p style="white-space:pre-wrap;" data-rte-preserve-empty="true">This past weekend, our BH Futures Academy community gathered in Zenica for one of the most meaningful moments of the year, the Graduation Ceremony of the 2025/26 Fellowship cycle.</p>
                    <a href="/news/2026/7/27/a-new-generation-joins-the-bh-futures-alumni-community" class="BlogList-item-readmore">
                      <span>Read More</span>
                    </a>
                  </div>



              <div class="Blog-meta BlogList-item-meta">
                <!--

                Author

                --><a href="/news?author=6329c21690377f6f3f80347b" class="Blog-meta-item Blog-meta-item--author">Marketing BHFF</a><!--

                Date

                --><time class="Blog-meta-item Blog-meta-item--date" datetime="2026-07-27">July 27, 2026</time>
              </div>



            </article>
        """ + Suffix;

        /// <summary>
        /// The same page after a hypothetical redesign: the container class survives, so the
        /// articles are still found, but every child selector has been renamed.
        ///
        /// This is the nastier of the two redesign shapes, and the reason the parser
        /// validates each post rather than only checking that it found some articles. A
        /// parser written with <c>?.TextContent ?? string.Empty</c> finds three containers
        /// here and cheerfully produces three posts with empty titles and no dates.
        /// </summary>
        public const string RedesignedInnerMarkup = Prefix + """
        <article class="BlogList-item">
          <a href="/news/2026/7/29/some-post" class="c-card__heading">A post whose title moved</a>
          <div class="c-card__summary"><p>The excerpt moved too.</p></div>
          <div class="c-card__meta">
            <span class="c-card__byline">Marketing BHFF</span>
            <span class="c-card__date">July 29, 2026</span>
          </div>
        </article>
        <article class="BlogList-item">
          <a href="/news/2026/7/27/another-post" class="c-card__heading">Another moved title</a>
        </article>
        <article class="BlogList-item">
          <a href="/news/2026/7/27/a-third-post" class="c-card__heading">A third moved title</a>
        </article>
        """ + Suffix;

        /// <summary>
        /// A redesign that renames the container itself, so nothing matches at all. Stands in
        /// equally for being served something that is not the news page - a login wall, a CDN
        /// error page, a bot check - all of which arrive as valid HTML under HTTP 200.
        /// </summary>
        public const string RedesignedContainer = Prefix + """
        <div class="c-post-list__item">
          <a href="/news/2026/7/29/some-post" class="c-card__heading">A post in a renamed container</a>
        </div>
        """ + Suffix;

        /// <summary>
        /// One well-formed post alongside one that lost its title.
        ///
        /// This is the middle ground between the two extremes above: a single malformed
        /// article must not blank the widget, so the good post is kept and the bad one is
        /// reported as a warning rather than stored empty.
        /// </summary>
        public const string OneGoodOneBroken = Prefix + """
        <article class="BlogList-item">
          <a href="/news/2026/7/29/good-post" class="BlogList-item-title">A perfectly good post</a>
          <div class="BlogList-item-excerpt"><p>With an excerpt.</p></div>
          <div class="Blog-meta BlogList-item-meta">
            <a href="/news?author=1" class="Blog-meta-item Blog-meta-item--author">Marketing BHFF</a>
            <time class="Blog-meta-item Blog-meta-item--date" datetime="2026-07-29">July 29, 2026</time>
          </div>
        </article>
        <article class="BlogList-item">
          <a href="/news/2026/7/27/no-title" class="SomethingElse"></a>
          <time class="Blog-meta-item Blog-meta-item--date" datetime="2026-07-27">July 27, 2026</time>
        </article>
        """ + Suffix;

        // Shared page chrome, so each fixture above contains only the part that differs.
        private const string Prefix =
            "<!doctype html><html lang=\"en-US\"><head><title>News</title></head><body>" +
            "<main><section class=\"BlogList\">";

        private const string Suffix = "</section></main></body></html>";
    }
}
