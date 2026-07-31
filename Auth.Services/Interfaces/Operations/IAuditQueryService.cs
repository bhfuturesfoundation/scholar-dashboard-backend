using Auth.Models.DTOs.Operations;
using Auth.Models.Response;

namespace Auth.Services.Interfaces.Operations
{
    public class AuditQuery
    {
        /// <summary>Exact prefix, e.g. "Scholars.Promoted" or just "Scholars.".</summary>
        public string? EventType { get; set; }

        /// <summary>Friendly grouping, e.g. "Authentication". Maps to a set of prefixes.</summary>
        public string? Category { get; set; }

        public string? UserId { get; set; }

        /// <summary>Free text across event type, payload and the acting user's name/email.</summary>
        public string? Search { get; set; }

        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    /// <summary>
    /// Reads the audit trail. Deliberately read-only — audit rows are never edited or
    /// deleted through the application, or the trail would not be worth keeping.
    /// </summary>
    public interface IAuditQueryService
    {
        Task<PagedResult<AuditEventDto>> SearchAsync(AuditQuery query, CancellationToken cancellationToken = default);

        Task<AuditFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
    }
}
