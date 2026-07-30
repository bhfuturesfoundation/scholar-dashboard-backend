using Auth.Models.Entities.Mailing;
using Auth.Services.Interfaces.Mailing;

namespace Auth.Services.Services.Mailing
{
    /// <summary>
    /// Classifies a firm into a <see cref="FirmType"/> by matching the type's editable
    /// keywords against the firm's name, website and email domain.
    ///
    /// Keyword scoring rather than first-match: "Raiffeisen Bank Leasing" contains keywords
    /// for both Bank and Leasing, and the longer, word-boundary-aligned match is the better
    /// answer. Matching on the folded text means "Štedionica" and "stedionica" behave
    /// identically, which matters for a directory of Bosnian firms.
    /// </summary>
    public class FirmCategorizer : IFirmCategorizer
    {
        public FirmCategorySuggestion Suggest(
            string? firmName,
            string? website,
            string? email,
            IEnumerable<FirmType> types)
        {
            var haystack = BuildHaystack(firmName, website, email);

            if (haystack.Length == 0)
                return FirmCategorySuggestion.None("Nothing to match against.");

            FirmType? best = null;
            var bestScore = 0;
            string? bestKeyword = null;

            foreach (var type in types)
            {
                foreach (var keyword in type.Keywords)
                {
                    var folded = TextNormalizer.FoldToWords(keyword);
                    if (folded.Length < 2) continue;

                    var score = ScoreKeyword(haystack, folded);
                    if (score <= bestScore) continue;

                    bestScore = score;
                    best = type;
                    bestKeyword = keyword;
                }
            }

            if (best is null)
                return FirmCategorySuggestion.None("No configured keyword matched.");

            return new FirmCategorySuggestion
            {
                FirmTypeId = best.Id,
                FirmTypeName = best.Name,
                MatchedKeyword = bestKeyword,
                // A whole-word hit is a confident classification; a substring hit inside a
                // longer word ("banka" inside "urbanka") is a guess worth reviewing.
                IsConfident = bestScore >= ConfidentBonus,
                Reason = $"Matched \"{bestKeyword}\" in the firm's name, website or domain."
            };
        }

        /// <summary>Added when a match is trustworthy enough to apply without review.</summary>
        private const int ConfidentBonus = 1000;

        /// <summary>
        /// Shortest keyword that may match as a word prefix. Below this, prefix matching is
        /// too loose — "it" would confidently classify "Italija Trade" as an IT company.
        /// </summary>
        private const int MinPrefixKeywordLength = 4;

        private static int ScoreKeyword(string haystack, string keyword)
        {
            var index = haystack.IndexOf(keyword, StringComparison.Ordinal);
            if (index < 0) return 0;

            var startsOnBoundary = index == 0 || haystack[index - 1] == ' ';
            var endIndex = index + keyword.Length;
            var endsOnBoundary = endIndex == haystack.Length || haystack[endIndex] == ' ';

            // Longer keywords win ties: "investment bank" beats "bank".
            var score = keyword.Length;

            // A whole-word hit is unambiguous.
            if (startsOnBoundary && endsOnBoundary)
                return score + ConfidentBonus;

            // A hit at the START of a word is also trustworthy, because Bosnian/Croatian/
            // Serbian inflect by suffix: "advokat" appears as "advokatska", "banka" as
            // "bankarstvo", "klinika" as "klinicki". Requiring the end boundary too would
            // miss the majority of real firm names in this market. Length-gated so short
            // keywords can't prefix-match unrelated words.
            if (startsOnBoundary && keyword.Length >= MinPrefixKeywordLength)
                return score + ConfidentBonus;

            // Mid-word substring — "banka" inside "urbanka". Recorded as a weak suggestion
            // for a human to confirm, never applied automatically.
            return score;
        }

        private static string BuildHaystack(string? firmName, string? website, string? email)
        {
            var parts = new List<string>();

            var name = TextNormalizer.FoldToWords(firmName);
            if (name.Length > 0) parts.Add(name);

            // The domain label often carries the sector when the trading name doesn't —
            // "info@sparkasse.ba" classifies where "Sparkasse" alone might not.
            var domain = TextNormalizer.FoldToWords(TextNormalizer.DomainPart(email));
            if (domain.Length > 0) parts.Add(domain);

            var site = TextNormalizer.FoldToWords(website);
            if (site.Length > 0) parts.Add(site);

            return string.Join(' ', parts);
        }
    }
}
