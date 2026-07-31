using Auth.Models.Data;
using Auth.Models.DTOs.Operations;
using Auth.Models.Response;
using Auth.Services.Interfaces.Operations;
using Microsoft.EntityFrameworkCore;

namespace Auth.Services.Services.Operations
{
    /// <summary>
    /// Read side of the audit trail.
    ///
    /// Everything already writes to <c>AuditEvent</c> — backups, cohort promotions, campaign
    /// sends, mentor changes, logins, role edits — but nothing could read it back, so the
    /// entire trail was write-only. This makes it answerable: who promoted that cohort, who
    /// took a backup with credentials in it, when did that campaign go out.
    /// </summary>
    public class AuditQueryService : IAuditQueryService
    {
        /// <summary>
        /// Prefixes grouped into categories for the filter dropdown, so an operator doesn't
        /// have to know the exact event-type strings.
        /// </summary>
        private static readonly Dictionary<string, string[]> Categories = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Authentication"] = new[] { "Login.", "Auth.", "Password.", "TwoFactor." },
            ["Scholars"] = new[] { "Scholars." },
            ["Backups & exports"] = new[] { "Backup.", "Export." },
            ["Mailing"] = new[] { "Mailing." },
            ["Administration"] = new[] { "Role.", "User.", "Question." },
        };

        private readonly ApplicationDbContext _context;

        public AuditQueryService(ApplicationDbContext context) => _context = context;

        public async Task<PagedResult<AuditEventDto>> SearchAsync(
            AuditQuery query, CancellationToken cancellationToken = default)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);

            var events = _context.AuditEvents
                .AsNoTracking()
                .Include(e => e.User)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.EventType))
                events = events.Where(e => e.EventType.StartsWith(query.EventType));

            if (!string.IsNullOrWhiteSpace(query.Category) &&
                Categories.TryGetValue(query.Category, out var prefixes))
            {
                // Translated to a chain of StartsWith rather than a client-side filter, so
                // paging still happens in the database.
                events = events.Where(e => prefixes.Any(p => e.EventType.StartsWith(p)));
            }

            if (!string.IsNullOrWhiteSpace(query.UserId))
                events = events.Where(e => e.UserId == query.UserId);

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim().ToLowerInvariant();
                events = events.Where(e =>
                    e.EventType.ToLower().Contains(term) ||
                    (e.Payload != null && e.Payload.ToLower().Contains(term)) ||
                    (e.User != null && (
                        (e.User.Email ?? "").ToLower().Contains(term) ||
                        (e.User.FirstName ?? "").ToLower().Contains(term) ||
                        (e.User.LastName ?? "").ToLower().Contains(term))));
            }

            if (query.From.HasValue) events = events.Where(e => e.Timestamp >= query.From);
            if (query.To.HasValue) events = events.Where(e => e.Timestamp <= query.To);

            var total = await events.CountAsync(cancellationToken);

            var items = await events
                .OrderByDescending(e => e.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new AuditEventDto
                {
                    Id = e.Id,
                    EventType = e.EventType,
                    Payload = e.Payload,
                    Timestamp = e.Timestamp,
                    IpAddress = e.IpAddress,
                    UserId = e.UserId,
                    UserDisplayName = e.User == null
                        ? null
                        : ((e.User.FirstName ?? "") + " " + (e.User.LastName ?? "")).Trim(),
                    UserEmail = e.User == null ? null : e.User.Email
                })
                .ToListAsync(cancellationToken);

            return new PagedResult<AuditEventDto>
            {
                Items = items,
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<AuditFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
        {
            // Distinct event types actually present, so the filter only ever offers something
            // that will return rows.
            var eventTypes = await _context.AuditEvents
                .AsNoTracking()
                .Select(e => e.EventType)
                .Distinct()
                .OrderBy(t => t)
                .Take(200)
                .ToListAsync(cancellationToken);

            return new AuditFilterOptionsDto
            {
                Categories = Categories.Keys.ToList(),
                EventTypes = eventTypes
            };
        }
    }
}
