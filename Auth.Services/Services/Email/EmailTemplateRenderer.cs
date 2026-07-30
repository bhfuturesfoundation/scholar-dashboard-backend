using Auth.Services.Interfaces.Email;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Auth.Services.Services.Email
{
    /// <summary>
    /// Expands <c>{{variable}}</c> placeholders and renders the FLS email layout.
    ///
    /// Two deliberate safety choices:
    ///  1. The body is treated as PLAIN TEXT and HTML-escaped before any markup is added.
    ///     Substituted values (speaker names, organisations) therefore cannot inject HTML,
    ///     which matters because a name containing an apostrophe or angle bracket used to
    ///     break the markup — or worse.
    ///  2. Substitution happens on the escaped text, so a value that itself contains
    ///     "{{something}}" is not re-expanded. Single-pass by construction.
    /// </summary>
    public partial class EmailTemplateRenderer : IEmailTemplateRenderer
    {
        // {{ name }} — tolerant of surrounding whitespace, letters/digits/underscore only.
        [GeneratedRegex(@"\{\{\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\}\}", RegexOptions.Compiled)]
        private static partial Regex PlaceholderRegex();

        // Bare http/https URLs in the plain-text body, linkified in the HTML view.
        [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.Compiled)]
        private static partial Regex UrlRegex();

        public RenderedEmail Render(
            string subjectTemplate,
            string bodyTemplate,
            IReadOnlyDictionary<string, string?> variables)
        {
            ArgumentNullException.ThrowIfNull(variables);

            var unresolved = new List<string>();

            // Subject is plain text in every mail client — substitute raw, no escaping.
            var subject = Substitute(subjectTemplate ?? string.Empty, variables, unresolved, htmlEscapeValues: false);

            // Text body: raw values, used for the plain-text alternative view.
            var textBody = Substitute(bodyTemplate ?? string.Empty, variables, unresolved, htmlEscapeValues: false);

            // HTML body: escape the template first, then substitute escaped values into it.
            var escapedTemplate = WebUtility.HtmlEncode(bodyTemplate ?? string.Empty);
            var escapedBody = Substitute(escapedTemplate, variables, unresolved: null, htmlEscapeValues: true);

            return new RenderedEmail
            {
                Subject = subject.Trim(),
                TextBody = textBody,
                HtmlBody = WrapInLayout(ToHtmlParagraphs(escapedBody)),
                UnresolvedVariables = unresolved.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        public IReadOnlyList<string> ExtractVariableNames(string template)
        {
            if (string.IsNullOrEmpty(template)) return Array.Empty<string>();

            return PlaceholderRegex()
                .Matches(template)
                .Select(m => m.Groups["name"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Substitute(
            string template,
            IReadOnlyDictionary<string, string?> variables,
            List<string>? unresolved,
            bool htmlEscapeValues)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            return PlaceholderRegex().Replace(template, match =>
            {
                var name = match.Groups["name"].Value;

                // Case-insensitive lookup so {{FirstName}} and {{firstname}} both work —
                // partner members type these by hand and shouldn't have to match casing.
                var value = FindValue(variables, name);

                if (value is null)
                {
                    unresolved?.Add(name);
                    // Drop the placeholder rather than shipping literal "{{firstName}}".
                    return string.Empty;
                }

                return htmlEscapeValues ? WebUtility.HtmlEncode(value) : value;
            });
        }

        private static string? FindValue(IReadOnlyDictionary<string, string?> variables, string name)
        {
            if (variables.TryGetValue(name, out var exact))
                return exact;

            foreach (var kvp in variables)
            {
                if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            return null;
        }

        /// <summary>
        /// Turns already-escaped text into paragraphs and line breaks, and linkifies URLs.
        /// Input must already be HTML-escaped — this method only adds markup.
        /// </summary>
        private static string ToHtmlParagraphs(string escapedText)
        {
            if (string.IsNullOrWhiteSpace(escapedText)) return string.Empty;

            var normalised = escapedText.Replace("\r\n", "\n").Replace('\r', '\n');
            var paragraphs = normalised.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

            var sb = new StringBuilder();
            foreach (var paragraph in paragraphs)
            {
                var withBreaks = paragraph.Trim('\n').Replace("\n", "<br />");
                var linkified = UrlRegex().Replace(withBreaks, m =>
                    $"<a href=\"{m.Value}\" style=\"color:#1d4ed8;text-decoration:underline;\">{m.Value}</a>");

                sb.Append("<p style=\"margin:0 0 16px 0;\">").Append(linkified).Append("</p>");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Table-based layout with inline styles — the only thing Outlook renders reliably.
        /// Flexbox and &lt;style&gt; blocks are stripped by several major clients.
        /// </summary>
        private static string WrapInLayout(string innerHtml) => $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
            </head>
            <body style="margin:0;padding:0;background-color:#f4f4f5;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f4f5;padding:24px 12px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px;background-color:#ffffff;border:2px solid #0b1b3d;">
                      <tr>
                        <td style="background-color:#0b1b3d;padding:20px 28px;">
                          <div style="color:#ffffff;font-family:Arial,Helvetica,sans-serif;font-size:13px;font-weight:bold;letter-spacing:2px;text-transform:uppercase;">
                            Future Leaders Summit
                          </div>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:28px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.6;color:#1f2937;">
                          {{innerHtml}}
                        </td>
                      </tr>
                      <tr>
                        <td style="border-top:1px solid #e5e7eb;padding:18px 28px;font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#6b7280;">
                          BH Futures Foundation &middot; Future Leaders Summit<br />
                          <a href="https://fls.ba" style="color:#6b7280;">fls.ba</a>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
