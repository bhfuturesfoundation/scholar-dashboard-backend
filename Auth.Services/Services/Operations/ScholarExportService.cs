using Auth.Models.Data;
using Auth.Services.Interfaces.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Operations
{
    /// <summary>
    /// Exports the scholar roster for program managers and admins.
    ///
    /// Contains only fields these roles already see in the UI — no password hashes, no
    /// tokens. That is what makes this safe to expose to program managers while full
    /// database backups stay Admin-only.
    /// </summary>
    public class ScholarExportService : IScholarExportService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ScholarExportService> _logger;

        public ScholarExportService(ApplicationDbContext context, ILogger<ScholarExportService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ExportTable> BuildAsync(
            ScholarExportFilter filter, CancellationToken cancellationToken = default)
        {
            var query = _context.Users.AsNoTracking().AsQueryable();

            // Default excludes deactivated accounts. Someone exporting "the scholars" means
            // the current cohort; deactivated accounts are opt-in and clearly labelled.
            query = filter.Include switch
            {
                ScholarInclusion.ActiveOnly => query.Where(u => u.IsActive),
                ScholarInclusion.InactiveOnly => query.Where(u => !u.IsActive),
                _ => query
            };

            var users = await query
                .OrderBy(u => u.LastName)
                .ThenBy(u => u.FirstName)
                .Select(u => new
                {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.Title,
                    u.IsActive,
                    u.EmailConfirmed,
                    u.CreatedAt,
                    MentorName = u.Mentor != null ? u.Mentor.FirstName + " " + u.Mentor.LastName : null
                })
                .ToListAsync(cancellationToken);

            var userIds = users.Select(u => u.Id).ToList();

            // Roles and submission counts fetched in two set-based queries rather than per
            // user — a per-user lookup over a few hundred scholars is what turns a 200 ms
            // export into a 30 second one.
            var roleNames = await _context.Roles
                .AsNoTracking()
                .ToDictionaryAsync(r => r.Id, r => r.Name ?? string.Empty, cancellationToken);

            var userRoles = await _context.UserRoles
                .AsNoTracking()
                .Where(ur => userIds.Contains(ur.UserId))
                .ToListAsync(cancellationToken);

            var rolesByUser = userRoles
                .GroupBy(ur => ur.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => string.Join(", ", g
                        .Select(ur => roleNames.TryGetValue(ur.RoleId, out var n) ? n : null)
                        .Where(n => !string.IsNullOrEmpty(n))));

            var submissionCounts = await _context.JournalSubmissions
                .AsNoTracking()
                .Where(js => userIds.Contains(js.ScholarId) && js.Submitted)
                .GroupBy(js => js.ScholarId)
                .Select(g => new { ScholarId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ScholarId, x => x.Count, cancellationToken);

            var table = new ExportTable
            {
                Name = "Scholars",
                Headers = new List<string>
                {
                    "First name", "Last name", "Email", "Status", "Roles",
                    "Account active", "Email confirmed", "Mentor",
                    "Journals submitted", "Member since"
                }
            };

            foreach (var user in users)
            {
                table.Rows.Add(new List<object?>
                {
                    user.FirstName,
                    user.LastName,
                    user.Email,
                    user.Title,
                    rolesByUser.TryGetValue(user.Id, out var roles) ? roles : string.Empty,
                    user.IsActive,
                    user.EmailConfirmed,
                    user.MentorName,
                    submissionCounts.TryGetValue(user.Id, out var count) ? count : 0,
                    user.CreatedAt
                });
            }

            _logger.LogInformation("Built scholar export: {Count} rows ({Filter}).", table.Rows.Count, filter.Include);

            return table;
        }
    }
}
