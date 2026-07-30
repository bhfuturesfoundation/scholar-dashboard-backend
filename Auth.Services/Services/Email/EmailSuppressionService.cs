using Auth.Models.Data;
using Auth.Models.Enums.Mailing;
using Auth.Services.Interfaces.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Email
{
    /// <summary>
    /// Decides whether an address may be mailed, by consulting three independent sources:
    /// the explicit suppression list, the user account, and the firm record.
    ///
    /// Checked inside <c>EmailDispatcher</c> so it cannot be bypassed. Every audience query
    /// also filters, but this is the backstop that makes a forgotten filter harmless.
    /// </summary>
    public class EmailSuppressionService : IEmailSuppressionService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<EmailSuppressionService> _logger;

        public EmailSuppressionService(ApplicationDbContext context, ILogger<EmailSuppressionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<SuppressionCheck> CheckAsync(string? email, CancellationToken cancellationToken = default)
        {
            var normalized = Normalize(email);

            if (normalized is null)
                return SuppressionCheck.Block(SuppressionReason.InvalidAddress, "No usable email address.");

            var results = await CheckManyAsync(new[] { normalized }, cancellationToken);

            return results.TryGetValue(normalized, out var check) ? check : SuppressionCheck.Allowed;
        }

        public async Task<IReadOnlyDictionary<string, SuppressionCheck>> CheckManyAsync(
            IEnumerable<string> emails, CancellationToken cancellationToken = default)
        {
            var normalized = emails
                .Select(Normalize)
                .Where(e => e is not null)
                .Select(e => e!)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var blocked = new Dictionary<string, SuppressionCheck>(StringComparer.Ordinal);

            if (normalized.Count == 0) return blocked;

            // Three targeted queries rather than one per recipient. A 400-firm campaign that
            // checked individually would issue 1,200 round trips before sending anything.

            // 1. Explicit suppression list — the strongest signal, so it's applied first and
            //    never overwritten by the checks below.
            var suppressed = await _context.EmailSuppressions
                .AsNoTracking()
                .Where(s => normalized.Contains(s.NormalizedEmail) && s.LiftedAt == null)
                .Select(s => new { s.NormalizedEmail, s.Reason })
                .ToListAsync(cancellationToken);

            foreach (var entry in suppressed)
            {
                blocked[entry.NormalizedEmail] = SuppressionCheck.Block(
                    SuppressionReason.ManuallySuppressed,
                    entry.Reason ?? "On the suppression list.");
            }

            // 2. Deactivated accounts. Deactivating a scholar has to mean they stop hearing
            //    from us — otherwise "deactivated" is a UI state with no real effect.
            var inactiveUsers = await _context.Users
                .AsNoTracking()
                .Where(u => !u.IsActive && u.Email != null && normalized.Contains(u.Email.ToLower()))
                .Select(u => u.Email!.ToLower())
                .ToListAsync(cancellationToken);

            foreach (var address in inactiveUsers)
            {
                if (blocked.ContainsKey(address)) continue;

                blocked[address] = SuppressionCheck.Block(
                    SuppressionReason.UserInactive,
                    "The account is deactivated.");
            }

            // 3. Firms that have opted out or gone bad.
            var blockedFirms = await _context.Firms
                .AsNoTracking()
                .Where(f => f.NormalizedEmail != null
                            && normalized.Contains(f.NormalizedEmail)
                            && f.Status != FirmStatus.Active)
                .Select(f => new { Email = f.NormalizedEmail!, f.Status })
                .ToListAsync(cancellationToken);

            foreach (var firm in blockedFirms)
            {
                if (blocked.ContainsKey(firm.Email)) continue;

                var (reason, explanation) = firm.Status switch
                {
                    FirmStatus.Unsubscribed => (SuppressionReason.FirmUnsubscribed, "The firm unsubscribed."),
                    FirmStatus.Bounced => (SuppressionReason.FirmBounced, "Mail to this address hard-bounced."),
                    FirmStatus.DoNotContact => (SuppressionReason.FirmDoNotContact, "The firm is marked do-not-contact."),
                    FirmStatus.Incomplete => (SuppressionReason.InvalidAddress, "The firm record has no usable address."),
                    _ => (SuppressionReason.FirmDoNotContact, "The firm is not contactable.")
                };

                blocked[firm.Email] = SuppressionCheck.Block(reason, explanation);
            }

            if (blocked.Count > 0)
            {
                _logger.LogInformation(
                    "Suppression check blocked {Blocked} of {Total} addresses.", blocked.Count, normalized.Count);
            }

            return blocked;
        }

        private static string? Normalize(string? email) =>
            string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }
}
