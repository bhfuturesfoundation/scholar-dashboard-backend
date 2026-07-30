using Auth.Models.Data;
using Auth.Models.DTOs.Scholars;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces.Scholars;
using Auth.Services.Services.Mailing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Scholars
{
    public class MentorAssignmentService : IMentorAssignmentService
    {
        /// <summary>Header spellings accepted by the pairing sheet, folded.</summary>
        private static readonly Dictionary<string, string[]> ColumnAliases = new(StringComparer.Ordinal)
        {
            ["mentoremail"] = new[]
            {
                "mentor email", "mentoremail", "mentor e mail", "mentor mail",
                "email mentora", "mentor eposta"
            },
            ["scholaremail"] = new[]
            {
                "scholar email", "scholaremail", "student email", "mentee email",
                "scholar e mail", "email studenta", "email skolarca"
            },
        };

        private const int MaxReportedIssues = 300;

        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<MentorAssignmentService> _logger;

        public MentorAssignmentService(
            ApplicationDbContext context,
            UserManager<User> userManager,
            ILogger<MentorAssignmentService> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<MentorAssignmentOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            var scholarIds = await RoleMemberIdsAsync("User", cancellationToken);
            var mentorIds = await RoleMemberIdsAsync("Mentor", cancellationToken);

            var scholars = await _context.Users
                .AsNoTracking()
                .Where(u => scholarIds.Contains(u.Id))
                .Select(u => new { u.Id, u.MentorId })
                .ToListAsync(cancellationToken);

            var caseloads = scholars
                .Where(s => s.MentorId != null)
                .GroupBy(s => s.MentorId!)
                .ToDictionary(g => g.Key, g => g.Count());

            return new MentorAssignmentOverviewDto
            {
                TotalScholars = scholars.Count,
                AssignedCount = scholars.Count(s => s.MentorId != null),
                UnassignedCount = scholars.Count(s => s.MentorId is null),
                MentorCount = mentorIds.Count,
                MentorsWithNoMentees = mentorIds.Count(id => !caseloads.ContainsKey(id)),
                LargestCaseload = caseloads.Count == 0 ? 0 : caseloads.Values.Max()
            };
        }

        public async Task<List<MenteeAssignmentDto>> GetScholarsAsync(
            bool onlyUnassigned = false, string? search = null, CancellationToken cancellationToken = default)
        {
            var scholarIds = await RoleMemberIdsAsync("User", cancellationToken);

            var query = _context.Users
                .AsNoTracking()
                .Include(u => u.Mentor)
                .Include(u => u.Generation)
                .Where(u => scholarIds.Contains(u.Id));

            if (onlyUnassigned) query = query.Where(u => u.MentorId == null);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLowerInvariant();
                query = query.Where(u =>
                    (u.FirstName ?? "").ToLower().Contains(term) ||
                    (u.LastName ?? "").ToLower().Contains(term) ||
                    (u.Email ?? "").ToLower().Contains(term));
            }

            var scholars = await query
                .OrderBy(u => u.LastName)
                .ThenBy(u => u.FirstName)
                .Take(1000)
                .ToListAsync(cancellationToken);

            return scholars.Select(u => new MenteeAssignmentDto
            {
                UserId = u.Id,
                DisplayName = $"{u.FirstName} {u.LastName}".Trim(),
                Email = u.Email,
                GenerationName = u.Generation?.Name,
                Status = u.ScholarStatus.ToString(),
                IsActive = u.IsActive,
                MentorId = u.MentorId,
                MentorName = u.Mentor is null ? null : $"{u.Mentor.FirstName} {u.Mentor.LastName}".Trim(),
                MentorEmail = u.Mentor?.Email
            }).ToList();
        }

        public async Task<List<MentorSummaryDto>> GetMentorsAsync(CancellationToken cancellationToken = default)
        {
            var mentorIds = await RoleMemberIdsAsync("Mentor", cancellationToken);

            var mentors = await _context.Users
                .AsNoTracking()
                .Where(u => mentorIds.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    u.IsActive,
                    // Counted in the projection so the picker doesn't fire a query per mentor.
                    MenteeCount = _context.Users.Count(s => s.MentorId == u.Id)
                })
                .ToListAsync(cancellationToken);

            return mentors
                .Select(m => new MentorSummaryDto
                {
                    UserId = m.Id,
                    DisplayName = $"{m.FirstName} {m.LastName}".Trim(),
                    Email = m.Email,
                    IsActive = m.IsActive,
                    MenteeCount = m.MenteeCount
                })
                .OrderBy(m => m.DisplayName)
                .ToList();
        }

        public async Task AssignAsync(string scholarId, string mentorId, CancellationToken cancellationToken = default)
        {
            var scholar = await _context.Users.FirstOrDefaultAsync(u => u.Id == scholarId, cancellationToken)
                ?? throw new NotFoundException("Scholar", scholarId);

            var mentor = await _context.Users.FirstOrDefaultAsync(u => u.Id == mentorId, cancellationToken)
                ?? throw new NotFoundException("Mentor", mentorId);

            if (!await _userManager.IsInRoleAsync(mentor, "Mentor"))
                throw new ValidationException($"{mentor.Email} is not in the Mentor role.");

            // Self-mentoring would create a cycle in a self-referencing FK and break every
            // query that walks the relationship.
            if (scholarId == mentorId)
                throw new ValidationException("A scholar cannot be their own mentor.");

            scholar.MentorId = mentorId;
            scholar.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task UnassignAsync(string scholarId, CancellationToken cancellationToken = default)
        {
            var scholar = await _context.Users.FirstOrDefaultAsync(u => u.Id == scholarId, cancellationToken)
                ?? throw new NotFoundException("Scholar", scholarId);

            scholar.MentorId = null;
            scholar.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<MentorPairingResultDto> ImportPairingsAsync(
            Stream fileStream,
            string fileName,
            MentorPairingOptions options,
            CancellationToken cancellationToken = default)
        {
            var (headers, rows) = SpreadsheetReader.Read(fileStream, fileName);

            var result = new MentorPairingResultDto
            {
                WasDryRun = options.DryRun,
                FileName = fileName,
                TotalRows = rows.Count,
                DetectedColumns = headers
            };

            var map = SpreadsheetReader.BuildColumnMap(headers, ColumnAliases);

            if (!map.ContainsKey("mentoremail") || !map.ContainsKey("scholaremail"))
            {
                result.FailedCount = rows.Count;
                result.Issues.Add(new MentorPairingIssueDto
                {
                    Outcome = "Failed",
                    Message = "The file needs a mentor-email column and a scholar-email column. " +
                              $"Found: {string.Join(", ", headers)}."
                });
                return result;
            }

            // Every account loaded once, keyed by lowercased email. The seeder did a
            // FindByEmailAsync per row, which is a round trip per line.
            var accounts = await _context.Users
                .Where(u => u.Email != null)
                .ToDictionaryAsync(u => u.Email!.ToLower(), cancellationToken);

            var mentorIds = await RoleMemberIdsAsync("Mentor", cancellationToken);
            var mentorIdSet = mentorIds.ToHashSet(StringComparer.Ordinal);

            for (var i = 0; i < rows.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = rows[i];
                var rowNumber = i + 2;

                var mentorEmail = SpreadsheetReader.Value(row, map, "mentoremail")?.Trim().ToLowerInvariant();
                var scholarEmail = SpreadsheetReader.Value(row, map, "scholaremail")?.Trim().ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(mentorEmail) && string.IsNullOrWhiteSpace(scholarEmail))
                    continue;

                if (string.IsNullOrWhiteSpace(scholarEmail))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, mentorEmail, scholarEmail, "Failed", "No scholar email in this row.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(mentorEmail))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, mentorEmail, scholarEmail, "Failed", "No mentor email in this row.");
                    continue;
                }

                if (!accounts.TryGetValue(scholarEmail, out var scholar))
                {
                    // The exact failure that silently produced 22 unmentored scholars on every
                    // boot. Reported as an actionable row rather than a log line.
                    result.FailedCount++;
                    AddIssue(result, rowNumber, mentorEmail, scholarEmail, "Failed",
                        "No account exists for this scholar. Add them through scholar intake first, or correct the address.");
                    continue;
                }

                if (!accounts.TryGetValue(mentorEmail, out var mentor))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, mentorEmail, scholarEmail, "Failed",
                        "No account exists for this mentor.");
                    continue;
                }

                if (!mentorIdSet.Contains(mentor.Id))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, mentorEmail, scholarEmail, "Failed",
                        "That account exists but is not in the Mentor role.");
                    continue;
                }

                if (scholar.Id == mentor.Id)
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, mentorEmail, scholarEmail, "Failed",
                        "A scholar cannot be their own mentor.");
                    continue;
                }

                if (scholar.MentorId == mentor.Id)
                {
                    result.UnchangedCount++;
                    continue;
                }

                if (scholar.MentorId is not null && !options.ReassignExisting)
                {
                    result.UnchangedCount++;
                    AddIssue(result, rowNumber, mentorEmail, scholarEmail, "Skipped",
                        "Already has a different mentor. Enable reassignment to change it.");
                    continue;
                }

                if (scholar.MentorId is not null) result.ReassignedCount++;
                else result.AssignedCount++;

                if (!options.DryRun)
                {
                    scholar.MentorId = mentor.Id;
                    scholar.UpdatedAt = DateTime.UtcNow;
                }
            }

            if (!options.DryRun) await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Mentor pairing ({Mode}) of {File}: {Assigned} assigned, {Reassigned} reassigned, {Failed} failed.",
                options.DryRun ? "dry run" : "committed", fileName,
                result.AssignedCount, result.ReassignedCount, result.FailedCount);

            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private async Task<List<string>> RoleMemberIdsAsync(string roleName, CancellationToken cancellationToken)
        {
            var roleId = await _context.Roles
                .Where(r => r.Name == roleName)
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (roleId is null) return new List<string>();

            return await _context.UserRoles
                .Where(ur => ur.RoleId == roleId)
                .Select(ur => ur.UserId)
                .ToListAsync(cancellationToken);
        }

        private static void AddIssue(
            MentorPairingResultDto result, int rowNumber, string? mentorEmail, string? scholarEmail,
            string outcome, string message)
        {
            if (result.Issues.Count >= MaxReportedIssues) return;

            result.Issues.Add(new MentorPairingIssueDto
            {
                RowNumber = rowNumber,
                MentorEmail = mentorEmail,
                ScholarEmail = scholarEmail,
                Outcome = outcome,
                Message = message
            });
        }
    }
}
