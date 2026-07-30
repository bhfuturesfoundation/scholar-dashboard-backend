namespace Auth.Services.Interfaces.Email
{
    /// <summary>Result of expanding a subject/body template for one recipient.</summary>
    public class RenderedEmail
    {
        public string Subject { get; init; } = string.Empty;

        /// <summary>Branded HTML, safe to send — all substituted values are HTML-escaped.</summary>
        public string HtmlBody { get; init; } = string.Empty;

        /// <summary>Plain-text equivalent for the multipart alternative view.</summary>
        public string TextBody { get; init; } = string.Empty;

        /// <summary>
        /// Placeholders present in the template that had no matching variable.
        /// The preview endpoint surfaces these so nobody broadcasts "Dear {{firstName}}".
        /// </summary>
        public IReadOnlyList<string> UnresolvedVariables { get; init; } = Array.Empty<string>();
    }

    public interface IEmailTemplateRenderer
    {
        /// <summary>
        /// Expands <c>{{variable}}</c> placeholders in the subject and body, then wraps the
        /// body in the FLS email layout.
        /// </summary>
        /// <param name="bodyTemplate">
        /// Plain text. Blank lines become paragraphs and bare URLs become links; the text is
        /// HTML-escaped first, so recipient data can never inject markup.
        /// </param>
        RenderedEmail Render(
            string subjectTemplate,
            string bodyTemplate,
            IReadOnlyDictionary<string, string?> variables);

        /// <summary>Placeholder names found in the given template text, without duplicates.</summary>
        IReadOnlyList<string> ExtractVariableNames(string template);
    }
}
