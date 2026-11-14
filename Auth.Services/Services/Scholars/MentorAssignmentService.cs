using Auth.Models.Data;
using Auth.Models.DTOs.Scholars;
using Auth.Models.Entities;
using Auth.Models.Entities.Scholars;
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
                "mentor 1 email", "mentor1 email", "first mentor email", "email mentora 1",
                "email mentora", "mentor eposta"
            },
            ["mentor2email"] = new[]
            {
                "mentor 2 email", "second mentor email", "mentor2 email", "mentor email 2",
                "email mentora 2", "drugi mentor email"
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

            var assignments = await _context.MentorAssignments
                .AsNoTracking()
                .Where(a => scholarIds.Contains(a.ScholarId))
                .Select(a => new { a.ScholarId, a.MentorId })
                .ToListAsync(cancellationToken);

            var assignedScholars = assignments.Select(a => a.ScholarId).ToHashSet();
            var caseloads = assignments.GroupBy(a => a.MentorId).ToDictionary(g => g.Key, g => g.Count());

            return new MentorAssignmentOverviewDto
            {
                TotalScholars = scholarIds.Count,
                AssignedCount = assignedScholars.Count,
                UnassignedCount = scholarIds.Count - assignedScholars.Count,
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
                .Include(u => u.MentorAssignments).ThenInclude(a => a.Mentor)
                .Include(u => u.Generation)
                .Where(u => scholarIds.Contains(u.Id));

            if (onlyUnassigned) query = query.Where(u => !u.MentorAssignments.Any());

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLowerInvariant();
                query = query.Where(u =>
                    (u.FirstName ?? "").ToLower().Contains(term) ||
                    (u.LastName ?? "").ToLower().Contains(term) ||
                    (u.Email ?? "").ToLower().Contains(term));
            }

            var scholars = await query
                .AsSplitQuery()
                .OrderBy(u => u.LastName)
                .ThenBy(u => u.FirstName)
                .Take(1000)
                .ToListAsync(cancellationToken);

            return scholars.Select(u =>
            {
                var mentors = u.MentorAssignments
                    .OrderBy(a => a.AssignedAt)
                    .ThenBy(a => a.Id)
                    .Select(a => new AssignedMentorDto
                    {
                        MentorId = a.MentorId,
                        Name = $"{a.Mentor!.FirstName} {a.Mentor.LastName}".Trim(),
                        Email = a.Mentor.Email,
                        AllowJournalAccess = a.AllowJournalAccess
                    })
                    .ToList();

                return new MenteeAssignmentDto
                {
                    UserId = u.Id,
                    DisplayName = $"{u.FirstName} {u.LastName}".Trim(),
                    Email = u.Email,
                    GenerationName = u.Generation?.Name,
                    Status = u.ScholarStatus.ToString(),
                    IsActive = u.IsActive,
                    MentorId = mentors.FirstOrDefault()?.MentorId,
                    MentorName = mentors.FirstOrDefault()?.Name,
                    MentorEmail = mentors.FirstOrDefault()?.Email,
                    Mentors = mentors
                };
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
                    MenteeCount = u.MenteeAssignments.Count
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
            if (scholarId == mentorId)
                throw new ValidationException("A scholar cannot be their own mentor.");

            if (!await _context.Users.AnyAsync(u => u.Id == scholarId, cancellationToken))
                throw new NotFoundException("Scholar", scholarId);

            var mentor = await _context.Users.FirstOrDefaultAsync(u => u.Id == mentorId, cancellationToken)
                ?? throw new NotFoundException("Mentor", mentorId);

            if (!await _userManager.IsInRoleAsync(mentor, "Mentor"))
                throw new ValidationException($"{mentor.Email} is not in the Mentor role.");

            var current = await _context.MentorAssignments
                .Where(a => a.ScholarId == scholarId)
                .Select(a => a.MentorId)
                .ToListAsync(cancellationToken);

            if (current.Contains(mentorId)) return;

            if (current.Count >= MentorAssignment.MaxMentorsPerScholar)
                throw new ValidationException(
                    $"This scholar already has {MentorAssignment.MaxMentorsPerScholar} mentors. Remove one first.");

            _context.MentorAssignments.Add(new MentorAssignment
            {
                ScholarId = scholarId,
                MentorId = mentorId,
                AssignedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task UnassignAsync(string scholarId, string? mentorId = null, CancellationToken cancellationToken = default)
        {
            var assignments = await _context.MentorAssignments
                .Where(a => a.ScholarId == scholarId && (mentorId == null || a.MentorId == mentorId))
                .ToListAsync(cancellationToken);

            if (assignments.Count == 0) throw new NotFoundException("Mentor assignment", scholarId);

            _context.MentorAssignments.RemoveRange(assignments);
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

            var mentorIdSet = (await RoleMemberIdsAsync("Mentor", cancellationToken)).ToHashSet(StringComparer.Ordinal);

            // Working copy of every scholar's mentors, so later rows see earlier ones.
            var existing = await _context.MentorAssignments.ToListAsync(cancellationToken);
            var mentorsOf = existing
                .GroupBy(a => a.ScholarId)
                .ToDictionary(g => g.Key, g => g.Select(a => a.MentorId).ToList());
            var replacedScholars = new HashSet<string>();

            for (var i = 0; i < rows.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = rows[i];
                var rowNumber = i + 2;

                var scholarEmail = SpreadsheetReader.Value(row, map, "scholaremail")?.Trim().ToLowerInvariant();
                var mentorEmails = new[] { "mentoremail", "mentor2email" }
                    .Select(column => SpreadsheetReader.Value(row, map, column)?.Trim().ToLowerInvariant())
                    .Where(email => !string.IsNullOrWhiteSpace(email))
                    .Select(email => email!)
                    .Distinct()
                    .ToList();

                if (mentorEmails.Count == 0 && string.IsNullOrWhiteSpace(scholarEmail))
                    continue;

                if (string.IsNullOrWhiteSpace(scholarEmail))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, mentorEmails.FirstOrDefault(), scholarEmail, "Failed", "No scholar email in this row.");
                    continue;
                }

                if (mentorEmails.Count == 0)
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, null, scholarEmail, "Failed", "No mentor email in this row.");
                    continue;
                }

                if (!accounts.TryGetValue(scholarEmail, out var scholar))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, mentorEmails[0], scholarEmail, "Failed",
                        "No account exists for this scholar. Add them through scholar intake first, or correct the address.");
                    continue;
                }

                if (!mentorsOf.TryGetValue(scholar.Id, out var current))
                {
                    current = new List<string>();
                    mentorsOf[scholar.Id] = current;
                }

                // Replacing clears what the scholar had the first time the file mentions them,
                // so a scholar listed on two rows ends up with both mentors from the file.
                if (options.ReassignExisting && replacedScholars.Add(scholar.Id) && current.Count > 0)
                {
                    if (!options.DryRun)
                    {
                        _context.MentorAssignments.RemoveRange(existing.Where(a => a.ScholarId == scholar.Id));
                    }

                    current.Clear();
                    result.ReassignedCount++;
                }

                foreach (var mentorEmail in mentorEmails)
                {
                    if (!accounts.TryGetValue(mentorEmail, out var mentor))
                    {
                        result.FailedCount++;
                        AddIssue(result, rowNumber, mentorEmail, scholarEmail, "Failed", "No account exists for this mentor.");
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
                        AddIssue(result, rowNumber, mentorEmail, scholarEmail, "Failed", "A scholar cannot be their own mentor.");
                        continue;
                    }

                    if (current.Contains(mentor.Id))
                    {
                        result.UnchangedCount++;
                        continue;
                    }

                    if (current.Count >= MentorAssignment.MaxMentorsPerScholar)
                    {
                        result.UnchangedCount++;
                        AddIssue(result, rowNumber, mentorEmail, scholarEmail, "Skipped",
                            $"Already has {MentorAssignment.MaxMentorsPerScholar} mentors. Turn on replacing to change them.");
                        continue;
                    }

                    current.Add(mentor.Id);
                    result.AssignedCount++;

                    if (!options.DryRun)
                    {
                        _context.MentorAssignments.Add(new MentorAssignment
                        {
                            ScholarId = scholar.Id,
                            MentorId = mentor.Id,
                            AssignedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            if (!options.DryRun) await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Mentor pairing ({Mode}) of {File}: {Assigned} assigned, {Reassigned} replaced, {Failed} failed.",
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
