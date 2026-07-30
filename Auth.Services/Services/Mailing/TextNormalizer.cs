using System.Globalization;
using System.Text;

namespace Auth.Services.Services.Mailing
{
    /// <summary>
    /// Shared text handling for the mailing module.
    ///
    /// Diacritic folding is not cosmetic here — it is load-bearing. Bosnian firm names are
    /// written "Štedionica", "Računovodstvo", "Hadžić", but the same organisation's email
    /// domain and the team's typed search terms will be "stedionica", "racunovodstvo",
    /// "hadzic". Without folding, keyword categorisation misses most local firms.
    /// </summary>
    public static class TextNormalizer
    {
        /// <summary>
        /// Lowercases and strips diacritics, so "Štedionica ĐĐ" → "stedionica dd".
        /// Handles the Bosnian/Croatian/Serbian letters that Unicode decomposition misses.
        /// </summary>
        public static string Fold(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            // Đ/đ and Ł/ł have no combining-mark decomposition — FormD leaves them intact,
            // so they have to be mapped by hand before the general pass.
            var pre = value
                .Replace("Đ", "D").Replace("đ", "d")
                .Replace("Ð", "D").Replace("ð", "d")
                .Replace("Ł", "L").Replace("ł", "l")
                .Replace("ß", "ss")
                .Replace("Æ", "AE").Replace("æ", "ae")
                .Replace("Ø", "O").Replace("ø", "o");

            var decomposed = pre.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);

            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant().Trim();
        }

        /// <summary>Folds, then collapses every run of non-alphanumeric characters to a single space.</summary>
        public static string FoldToWords(string? value)
        {
            var folded = Fold(value);
            if (folded.Length == 0) return string.Empty;

            var sb = new StringBuilder(folded.Length);
            var lastWasSpace = true;

            foreach (var ch in folded)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                    lastWasSpace = false;
                }
                else if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Title-cases a name token, preserving internal separators so hyphenated and
        /// apostrophised surnames survive: "hadzi-omerovic" → "Hadzi-Omerovic",
        /// "o'brien" → "O'Brien".
        /// </summary>
        public static string CapitalizeName(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return string.Empty;

            var sb = new StringBuilder(token.Length);
            var capitalizeNext = true;

            foreach (var ch in token.Trim())
            {
                if (ch is '-' or '\'' or '’' or ' ')
                {
                    sb.Append(ch);
                    capitalizeNext = true;
                    continue;
                }

                sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                capitalizeNext = false;
            }

            return sb.ToString();
        }

        /// <summary>Lowercased, trimmed email for the unique index. Null for blank input.</summary>
        public static string? NormalizeEmail(string? email) =>
            string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        /// <summary>Local part of an email (before @), or empty when there isn't one.</summary>
        public static string LocalPart(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return string.Empty;
            var at = email.IndexOf('@');
            return at <= 0 ? string.Empty : email[..at].Trim().ToLowerInvariant();
        }

        /// <summary>Domain part of an email (after @), or empty when there isn't one.</summary>
        public static string DomainPart(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return string.Empty;
            var at = email.IndexOf('@');
            return at < 0 || at == email.Length - 1 ? string.Empty : email[(at + 1)..].Trim().ToLowerInvariant();
        }

        /// <summary>
        /// The registrable-ish label of a domain: "mail.acme.co.uk" → "acme".
        /// Good enough to tell whether a mailbox name just repeats the company name.
        /// </summary>
        public static string DomainLabel(string? email)
        {
            var domain = DomainPart(email);
            if (domain.Length == 0) return string.Empty;

            var parts = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1) return domain;

            // Skip common second-level suffixes so "acme.co.uk" and "acme.com.ba" both
            // yield "acme" rather than "co" / "com".
            var suffixes = new[] { "co", "com", "org", "net", "gov", "edu", "ac", "mil" };

            for (var i = parts.Length - 2; i >= 0; i--)
            {
                if (!suffixes.Contains(parts[i]))
                    return parts[i];
            }

            return parts[0];
        }

        /// <summary>Slug for taxonomy rows: "Law Firm & Notary" → "law-firm-notary".</summary>
        public static string Slugify(string? value)
        {
            var words = FoldToWords(value);
            return words.Length == 0 ? string.Empty : words.Replace(' ', '-');
        }
    }
}
