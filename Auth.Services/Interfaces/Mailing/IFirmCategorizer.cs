using Auth.Models.Entities.Mailing;

namespace Auth.Services.Interfaces.Mailing
{
    public class FirmCategorySuggestion
    {
        public int? FirmTypeId { get; init; }
        public string? FirmTypeName { get; init; }

        /// <summary>The keyword that produced the match, shown in the review table.</summary>
        public string? MatchedKeyword { get; init; }

        /// <summary>
        /// True for a whole-word match. Bulk categorisation applies confident suggestions
        /// automatically and leaves the rest for a human to confirm.
        /// </summary>
        public bool IsConfident { get; init; }

        public string? Reason { get; init; }

        public bool HasSuggestion => FirmTypeId.HasValue;

        public static FirmCategorySuggestion None(string reason) => new() { Reason = reason };
    }

    /// <summary>
    /// Suggests a firm type from a firm's name, website and email domain, using the keywords
    /// configured on each <see cref="FirmType"/>.
    ///
    /// Keywords live on the type rather than in code so the partnerships team can teach the
    /// categoriser a new term the moment they hit a firm it missed, without a deploy.
    /// </summary>
    public interface IFirmCategorizer
    {
        FirmCategorySuggestion Suggest(
            string? firmName,
            string? website,
            string? email,
            IEnumerable<FirmType> types);
    }
}
