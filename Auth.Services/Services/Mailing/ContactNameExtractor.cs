using Auth.Models.Enums.Mailing;
using Auth.Services.Interfaces.Mailing;
using System.Text.RegularExpressions;

namespace Auth.Services.Services.Mailing
{
    /// <summary>
    /// Rule-based name derivation. Ordered most-reliable-first, and biased heavily toward
    /// returning nothing over returning a guess: a firm with no detected name still gets a
    /// perfectly good email via the firm-addressed template, whereas "Dear Prodaja" is an
    /// embarrassment sent to a potential sponsor.
    /// </summary>
    public partial class ContactNameExtractor : IContactNameExtractor
    {
        /// <summary>
        /// Shared/functional mailboxes, English and Bosnian/Croatian/Serbian. Compared after
        /// diacritic folding, so "podrška" only needs its folded form here.
        ///
        /// The local list matters: a directory of BH firms is full of kontakt@, uprava@ and
        /// direkcija@, none of which an English-only list would catch.
        /// </summary>
        private static readonly HashSet<string> GenericMailboxes = new(StringComparer.Ordinal)
        {
            // English
            "info", "contact", "contacts", "office", "hello", "hi", "hey", "admin",
            "administrator", "administration", "sales", "support", "help", "helpdesk",
            "hr", "jobs", "careers", "career", "recruitment", "marketing", "pr", "press",
            "media", "legal", "finance", "accounting", "accounts", "billing", "invoice",
            "invoices", "noreply", "no-reply", "donotreply", "do-not-reply", "mail",
            "email", "webmaster", "postmaster", "hostmaster", "abuse", "team", "all",
            "everyone", "general", "enquiries", "inquiries", "enquiry", "inquiry",
            "service", "services", "customerservice", "customercare", "cs", "booking",
            "bookings", "reservations", "events", "event", "partnership", "partnerships",
            "sponsorship", "sponsorships", "students", "student", "enrollment", "apply",
            "applications", "orders", "order", "shop", "store", "web", "website", "news",
            "newsletter", "subscribe", "unsubscribe", "feedback", "welcome", "main",

            // Bosnian / Croatian / Serbian (folded — no diacritics)
            "kontakt", "ured", "uprava", "direkcija", "direktor", "sekretar", "sekretarica",
            "tajnica", "tajnik", "recepcija", "prodaja", "nabavka", "nabava", "komercijala",
            "komercijalno", "podrska", "pomoc", "racunovodstvo", "knjigovodstvo", "finansije",
            "financije", "pravna", "pravnasluzba", "ljudskiresursi", "kadrovska", "posao",
            "poslovi", "zaposlenje", "opste", "opsti", "sluzba", "centrala", "arhiva",
            "informacije", "prijava", "prijave", "upis", "narudzbe", "narudzba", "ponude",
            "ponuda", "reklamacije", "servis", "skladiste", "logistika", "dogadjaji",
            "sponzorstvo", "saradnja", "obavjestenja", "obavijesti"
        };

        /// <summary>
        /// Titles that mark a personal name inside a firm name: "Advokat Amir Hodzic",
        /// "Dr Selma Begic Ordinacija". Folded, no trailing dot.
        /// </summary>
        private static readonly HashSet<string> PersonalTitles = new(StringComparer.Ordinal)
        {
            "dr", "prof", "mr", "mrs", "ms", "mag", "ing", "dipl", "spec", "prim",
            "advokat", "advokatica", "odvjetnik", "notar", "notarka", "biljeznik",
            "arhitekt", "arhitekta", "stomatolog", "ljekar", "doktor", "vet", "veterinar"
        };

        /// <summary>
        /// Corporate-form tokens to drop before deciding whether a firm name contains a
        /// person: "d.o.o.", "d.d.", "s.p.", "ltd", "gmbh".
        /// </summary>
        private static readonly HashSet<string> LegalFormTokens = new(StringComparer.Ordinal)
        {
            "doo", "dd", "sp", "sr", "ood", "ltd", "llc", "inc", "gmbh", "ag", "sa", "bv",
            "nv", "plc", "kg", "ohg", "eood", "zoo", "co", "company", "corp", "corporation",
            "group", "grupa", "holding", "trade", "trgovina", "komerc", "kompanija"
        };

        /// <summary>
        /// Unambiguous word boundaries. Dots, underscores and plus signs are never part of a
        /// surname, so splitting on them is always safe.
        /// </summary>
        [GeneratedRegex(@"[._+]+", RegexOptions.Compiled)]
        private static partial Regex PrimarySeparatorRegex();

        /// <summary>
        /// Hyphens are ambiguous: a separator in "john-smith@", but part of the name in
        /// "hadzi-omerovic@" — double-barrelled surnames are common in the region. Only used
        /// when the primary separators produced a single token, so "amina.hadzi-omerovic"
        /// keeps its surname intact while "john-smith" still splits.
        /// </summary>
        [GeneratedRegex(@"-+", RegexOptions.Compiled)]
        private static partial Regex HyphenRegex();

        /// <summary>Every separator, for deciding whether a mailbox has a generic head.</summary>
        [GeneratedRegex(@"[._\-+]+", RegexOptions.Compiled)]
        private static partial Regex AnySeparatorRegex();

        // Trailing digits: "amir.hodzic2" / "a.hodzic01" — an index, not part of the name.
        [GeneratedRegex(@"\d+$", RegexOptions.Compiled)]
        private static partial Regex TrailingDigitsRegex();

        public bool IsGenericMailbox(string? email)
        {
            var local = StripTrailingDigits(TextNormalizer.Fold(TextNormalizer.LocalPart(email)));
            if (local.Length == 0) return false;

            if (GenericMailboxes.Contains(local)) return true;

            // "info.sarajevo@", "prodaja-bl@", "hr_team@" — a generic head with a qualifier
            // is still a shared mailbox.
            var segments = AnySeparatorRegex().Split(local).Where(s => s.Length > 0).ToArray();
            return segments.Length > 0 && GenericMailboxes.Contains(segments[0]);
        }

        public ExtractedContactName Extract(string? email, string? firmName = null)
        {
            var fromEmail = ExtractFromEmail(email, firmName);
            if (fromEmail.HasName) return fromEmail;

            var fromName = ExtractFromFirmName(firmName);
            if (fromName.HasName) return fromName;

            // Prefer the email's explanation — it's the more specific one.
            return fromEmail.Reason is not null ? fromEmail : fromName;
        }

        private ExtractedContactName ExtractFromEmail(string? email, string? firmName)
        {
            var rawLocal = TextNormalizer.LocalPart(email);
            if (rawLocal.Length == 0)
                return ExtractedContactName.None("No email address to derive a name from.");

            var local = StripTrailingDigits(TextNormalizer.Fold(rawLocal));
            if (local.Length == 0)
                return ExtractedContactName.None("Mailbox name is only digits.");

            if (IsGenericMailbox(email))
                return ExtractedContactName.None($"Shared mailbox ({rawLocal}@) — not a person.");

            // A mailbox that just repeats the company or domain is the company's, not a
            // person's: acme@acme.ba, sarajevoosiguranje@sarajevoosiguranje.ba.
            var domainLabel = TextNormalizer.DomainLabel(email);
            var firmCompact = TextNormalizer.FoldToWords(firmName).Replace(" ", "");
            var localCompact = local.Replace("-", "").Replace(".", "").Replace("_", "");

            if (localCompact.Length > 0 &&
                (localCompact == domainLabel ||
                 (firmCompact.Length > 3 && localCompact == firmCompact)))
            {
                return ExtractedContactName.None($"Mailbox repeats the company name ({rawLocal}@).");
            }

            var tokens = Tokenize(local);

            // Two or more separated alphabetic tokens is the strong signal: first.last.
            if (tokens.Length >= 2)
            {
                var first = tokens[0];
                var last = tokens[^1];

                // A single-letter lead is an initial — we have a surname but no given name,
                // so "Dear A. Hodzic" would read badly. Record it, but mark it unusable.
                if (first.Length == 1)
                {
                    var surname = TextNormalizer.CapitalizeName(last);
                    return new ExtractedContactName
                    {
                        FullName = $"{first.ToUpperInvariant()}. {surname}",
                        FirstName = null,
                        LastName = surname,
                        Source = ContactNameSource.DerivedFromEmail,
                        Confidence = NameConfidence.Low,
                        Reason = "Initial and surname only — no given name to address."
                    };
                }

                if (first.Length >= 2 && last.Length >= 2)
                {
                    // Middle tokens are dropped: "amir.b.hodzic" addresses as "Amir Hodzic".
                    var firstName = TextNormalizer.CapitalizeName(first);
                    var lastName = TextNormalizer.CapitalizeName(last);

                    return new ExtractedContactName
                    {
                        FullName = $"{firstName} {lastName}",
                        FirstName = firstName,
                        LastName = lastName,
                        Source = ContactNameSource.DerivedFromEmail,
                        Confidence = NameConfidence.High,
                        Reason = $"Derived from {rawLocal}@ (given name and surname)."
                    };
                }
            }

            // Single token. A plausible given name is usable; anything long is probably a
            // concatenation we can't split reliably ("amirhodzic" — where does it break?).
            if (tokens.Length == 1)
            {
                var token = tokens[0];

                if (token.Length is >= 2 and <= 12)
                {
                    var name = TextNormalizer.CapitalizeName(token);
                    return new ExtractedContactName
                    {
                        FullName = name,
                        FirstName = name,
                        Source = ContactNameSource.DerivedFromEmail,
                        Confidence = NameConfidence.Medium,
                        Reason = $"Derived from {rawLocal}@ (given name only)."
                    };
                }

                if (token.Length > 12)
                {
                    var name = TextNormalizer.CapitalizeName(token);
                    return new ExtractedContactName
                    {
                        FullName = name,
                        Source = ContactNameSource.DerivedFromEmail,
                        Confidence = NameConfidence.Low,
                        Reason = "Mailbox looks like a run-together name — needs a human to split."
                    };
                }
            }

            return ExtractedContactName.None($"Could not read a name from {rawLocal}@.");
        }

        /// <summary>
        /// Looks for a personal name inside the firm name, which only works when a title
        /// marks it: "Advokat Amir Hodzic" yes, "Acme Trading" no. Without a title there is
        /// no way to tell a surname from a brand, so this deliberately gives up.
        /// </summary>
        private ExtractedContactName ExtractFromFirmName(string? firmName)
        {
            var words = TextNormalizer.FoldToWords(firmName);
            if (words.Length == 0)
                return ExtractedContactName.None("No firm name to derive from.");

            var originalTokens = (firmName ?? string.Empty)
                .Split(new[] { ' ', ',', '.', '-', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

            var foldedTokens = originalTokens.Select(TextNormalizer.Fold).ToArray();

            for (var i = 0; i < foldedTokens.Length; i++)
            {
                if (!PersonalTitles.Contains(foldedTokens[i])) continue;

                // Collect the following tokens that look like name parts, stopping at a
                // legal-form token or anything non-alphabetic.
                var nameParts = new List<string>();

                for (var j = i + 1; j < originalTokens.Length && nameParts.Count < 2; j++)
                {
                    var folded = foldedTokens[j];

                    if (folded.Length < 2) continue;
                    if (LegalFormTokens.Contains(folded)) break;
                    if (PersonalTitles.Contains(folded)) continue;
                    if (!folded.All(char.IsLetter)) break;

                    nameParts.Add(TextNormalizer.CapitalizeName(originalTokens[j]));
                }

                if (nameParts.Count == 0) continue;

                var full = string.Join(' ', nameParts);

                return new ExtractedContactName
                {
                    FullName = full,
                    FirstName = nameParts[0],
                    LastName = nameParts.Count > 1 ? nameParts[1] : null,
                    Source = ContactNameSource.DerivedFromFirmName,
                    // Two parts after a title is a confident read; one is a guess.
                    Confidence = nameParts.Count > 1 ? NameConfidence.Medium : NameConfidence.Low,
                    Reason = $"Derived from the firm name after the title \"{originalTokens[i]}\"."
                };
            }

            return ExtractedContactName.None("Firm name has no personal-name marker.");
        }

        /// <summary>
        /// Splits a mailbox name into candidate name parts.
        ///
        /// Unambiguous separators are tried first so a hyphenated surname survives; the
        /// hyphen is only treated as a separator when nothing else split the string, which is
        /// the only case where "john-smith" needs it.
        /// </summary>
        private static string[] Tokenize(string local)
        {
            var primary = PrimarySeparatorRegex()
                .Split(local)
                .Where(IsNameToken)
                .ToArray();

            if (primary.Length >= 2) return primary;

            // Single token: the hyphen, if there is one, is doing the separating.
            var single = primary.Length == 1 ? primary[0] : local;

            if (!single.Contains('-')) return primary;

            var hyphenSplit = HyphenRegex()
                .Split(single)
                .Where(IsNameToken)
                .ToArray();

            return hyphenSplit.Length >= 2 ? hyphenSplit : primary;
        }

        /// <summary>Letters, optionally joined by internal hyphens or apostrophes.</summary>
        private static bool IsNameToken(string token) =>
            token.Length > 0 &&
            char.IsLetter(token[0]) &&
            char.IsLetter(token[^1]) &&
            token.All(c => char.IsLetter(c) || c is '-' or '\'');

        private static string StripTrailingDigits(string local) =>
            TrailingDigitsRegex().Replace(local, string.Empty);
    }
}
