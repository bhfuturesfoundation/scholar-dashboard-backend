using System.Text;
using System.Text.Json;
using Auth.Models.Data;
using Auth.Models.DTOs.Account;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    /// <inheritdoc cref="IAccountService"/>
    public class AccountService : IAccountService
    {
        private const int MaxNameLength = 100;

        private static readonly JsonSerializerOptions ExportOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ITokenService _tokenService;
        private readonly ITokenVersionCache _tokenVersions;
        private readonly IAuditService _auditService;
        private readonly ILogger<AccountService> _logger;

        public AccountService(
            ApplicationDbContext context,
            UserManager<User> userManager,
            ITokenService tokenService,
            ITokenVersionCache tokenVersions,
            IAuditService auditService,
            ILogger<AccountService> logger)
        {
            _context = context;
            _userManager = userManager;
            _tokenService = tokenService;
            _tokenVersions = tokenVersions;
            _auditService = auditService;
            _logger = logger;
        }

        // ── Overview ──────────────────────────────────────────────────────────

        public async Task<AccountOverviewDto> GetOverviewAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Generation)
                .Include(u => u.Mentor)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException("User", userId);

            var roles = await _userManager.GetRolesAsync(user);

            var activeSessions = await _context.RefreshTokens
                .CountAsync(t => t.UserId == userId
                              && t.RevokedAt == null
                              && t.ExpiryTime > DateTime.UtcNow,
                    cancellationToken);

            var externalLogins = (await _userManager.GetLoginsAsync(user))
                .Select(l => l.LoginProvider)
                .Distinct()
                .ToList();

            var isScholar = roles.Contains(Models.Constants.AppRoles.User);

            return new AccountOverviewDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                Title = user.Title ?? string.Empty,
                Roles = roles.ToList(),
                IsActive = user.IsActive,
                EmailConfirmed = user.EmailConfirmed,
                CreatedAt = user.CreatedAt,

                // Only meaningful for scholars; staff accounts leave these null rather than
                // reporting "Unassigned", which would read as a problem to fix.
                ScholarStatus = isScholar ? (int)user.ScholarStatus : null,
                GenerationName = user.Generation?.Name,
                MentorName = user.Mentor is null
                    ? null
                    : $"{user.Mentor.FirstName} {user.Mentor.LastName}".Trim(),
                MentorJournalAccess = user.MentorId is null ? null : user.AllowMentorJournalAccess,

                TwoFactorEnabled = user.TwoFactorEnabled,

                // Hardcoded false because it is false. UserService clears TwoFactorEnabled on
                // every sign-in, so the feature is off platform-wide regardless of the flag on
                // the row. Reporting it honestly is better than a toggle that appears to work.
                TwoFactorAvailable = false,

                ActiveSessions = activeSessions,
                ExternalLogins = externalLogins
            };
        }

        // ── Profile ───────────────────────────────────────────────────────────

        public async Task<AccountOverviewDto> UpdateProfileAsync(
            string userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException("User", userId);

            var firstName = Clean(request.FirstName);
            var lastName = Clean(request.LastName);

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                throw new ValidationException("Both a first and last name are required.");
            }

            var before = $"{user.FirstName} {user.LastName}".Trim();

            user.FirstName = firstName;
            user.LastName = lastName;
            user.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            // Audited because a name change moves how this person appears in every journal
            // the staff read, in kudos other scholars sent them, and in exports. It is
            // legitimate self-service, but it should be traceable.
            await _auditService.LogAsync(
                "Account.ProfileUpdated",
                userId,
                $"Name changed from '{before}' to '{firstName} {lastName}'.");

            _logger.LogInformation("User {UserId} updated their own profile name.", userId);

            return await GetOverviewAsync(userId, cancellationToken);
        }

        /// <summary>
        /// Trims, collapses internal whitespace and caps length.
        ///
        /// Names go into email greetings and exported spreadsheets, so a trailing newline or
        /// a two-hundred-character paste is worth removing at the boundary rather than
        /// discovering later in a CSV.
        /// </summary>
        private static string? Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var collapsed = string.Join(' ', value.Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            return collapsed.Length <= MaxNameLength ? collapsed : collapsed[..MaxNameLength];
        }

        // ── Sessions ──────────────────────────────────────────────────────────

        public async Task<int> SignOutEverywhereAsync(
            string userId, string? ipAddress = null, CancellationToken cancellationToken = default)
        {
            var live = await _context.RefreshTokens
                .CountAsync(t => t.UserId == userId
                              && t.RevokedAt == null
                              && t.ExpiryTime > DateTime.UtcNow,
                    cancellationToken);

            await _tokenService.RevokeAllRefreshTokensAsync(
                userId, ipAddress, "Signed out of all devices by the account holder");

            // Revoking refresh tokens only stops renewal. Bumping the token version is what
            // kills the access tokens already out there: every one of them carries the
            // generation it was minted under, and authentication now rejects a mismatch.
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is not null)
            {
                user.TokenVersion += 1;
                await _context.SaveChangesAsync(cancellationToken);
            }

            // Evict immediately so the very next request re-reads it rather than waiting for
            // the cache entry to lapse.
            _tokenVersions.Invalidate(userId);

            await _auditService.LogAsync(
                "Account.SignedOutEverywhere",
                userId,
                $"Revoked {live} refresh token(s).");

            _logger.LogInformation(
                "User {UserId} signed out of all devices ({Count} session(s)).", userId, live);

            return live;
        }

        // ── Data export ───────────────────────────────────────────────────────

        public async Task<byte[]> ExportOwnDataAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Generation)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException("User", userId);

            var answers = await _context.Answers
                .AsNoTracking()
                .Where(a => a.ScholarId == userId)
                .OrderBy(a => a.MonthYear)
                .Select(a => new
                {
                    a.MonthYear,

                    // The snapshot, not the live question row. A question edited since this
                    // answer was written must not silently rewrite what the scholar was asked —
                    // that is the whole point of the snapshot column.
                    Question = a.QuestionTextSnapshot ?? a.Question.Text,
                    QuestionType = a.QuestionTypeSnapshot ?? a.Question.Type,
                    a.Response,
                    a.SubmittedAt,
                    a.IsSubmitted
                })
                .ToListAsync(cancellationToken);

            var achievements = await _context.Achievements
                .AsNoTracking()
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.EarnedAt)
                .Select(a => new { a.Key, a.EarnedAt })
                .ToListAsync(cancellationToken);

            var kudosReceived = await _context.Kudos
                .AsNoTracking()
                .Where(k => k.ToUserId == userId && !k.IsHidden)
                .OrderBy(k => k.CreatedAt)
                .Select(k => new
                {
                    From = k.FromUser.FirstName + " " + k.FromUser.LastName,
                    k.Category,
                    k.Message,
                    k.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var kudosGiven = await _context.Kudos
                .AsNoTracking()
                .Where(k => k.FromUserId == userId)
                .OrderBy(k => k.CreatedAt)
                .Select(k => new
                {
                    To = k.ToUser.FirstName + " " + k.ToUser.LastName,
                    k.Category,
                    k.Message,
                    k.CreatedAt
                })
                .ToListAsync(cancellationToken);

            var export = new
            {
                exportedAtUtc = DateTime.UtcNow,
                profile = new
                {
                    user.Email,
                    user.FirstName,
                    user.LastName,
                    user.Title,
                    Generation = user.Generation?.Name,
                    Status = user.ScholarStatus.ToString(),
                    user.CreatedAt
                },
                journalAnswers = answers,
                achievements,
                kudosReceived,
                kudosGiven
            };

            _logger.LogInformation(
                "User {UserId} exported their own data ({Answers} answers).", userId, answers.Count);

            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(export, ExportOptions));
        }
    }
}
