using Auth.Models.Data;
using Auth.Models.DTOs.Scholars;
using Auth.Models.Entities;
using Auth.Models.Entities.Scholars;
using Auth.Models.Enums.Scholars;
using Auth.Models.Exceptions;
using Auth.Models.Request.Scholars;
using Auth.Services.Interfaces.Scholars;
using Auth.Services.Interfaces.Storage;
using Auth.Services.Services.Mailing;
using Auth.Services.Services.Operations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Auth.Services.Services.Scholars
{
    public class ScholarLifecycleService : IScholarLifecycleService
    {
        /// <summary>Header spellings accepted by the intake sheet, folded.</summary>
        private static readonly Dictionary<string, string[]> ColumnAliases = new(StringComparer.Ordinal)
        {
            ["firstname"] = new[] { "first name", "firstname", "first", "given name", "ime" },
            ["lastname"] = new[] { "last name", "lastname", "surname", "family name", "prezime" },
            ["fullname"] = new[] { "name", "full name", "fullname", "ime i prezime", "scholar" },
            ["email"] = new[] { "email", "e mail", "mail", "email address", "eposta", "e posta" },
            ["title"] = new[] { "title", "status", "scholar status", "note", "napomena" },
        };

        private const int MaxReportedIssues = 200;

        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IDropboxStorage _dropbox;
        private readonly ILogger<ScholarLifecycleService> _logger;

        public ScholarLifecycleService(
            ApplicationDbContext context,
            UserManager<User> userManager,
            IDropboxStorage dropbox,
            ILogger<ScholarLifecycleService> logger)
        {
            _context = context;
            _userManager = userManager;
            _dropbox = dropbox;
            _logger = logger;
        }

        // ── Generations ───────────────────────────────────────────────────────

        public async Task<List<ScholarGenerationDto>> GetGenerationsAsync(CancellationToken cancellationToken = default)
        {
            // Counts computed in the projection so the list screen doesn't fire a query per row.
            return await _context.ScholarGenerations
                .AsNoTracking()
                .OrderByDescending(g => g.Year)
                .Select(g => new ScholarGenerationDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    Year = g.Year,
                    Description = g.Description,
                    StartsOn = g.StartsOn,
                    EndsOn = g.EndsOn,
                    IsCurrent = g.IsCurrent,
                    CreatedAt = g.CreatedAt,
                    TotalScholars = g.Scholars.Count,
                    JuniorCount = g.Scholars.Count(s => s.ScholarStatus == ScholarStatus.Junior),
                    SeniorCount = g.Scholars.Count(s => s.ScholarStatus == ScholarStatus.Senior),
                    AlumniCount = g.Scholars.Count(s => s.ScholarStatus == ScholarStatus.Alumni)
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<ScholarGenerationDto> CreateGenerationAsync(
            UpsertGenerationRequest request, string? userId, CancellationToken cancellationToken = default)
        {
            Validate(request);

            if (await _context.ScholarGenerations.AnyAsync(g => g.Year == request.Year, cancellationToken))
                throw new ConflictException($"A generation for {request.Year} already exists.");

            var generation = new ScholarGeneration
            {
                Name = request.Name.Trim(),
                Year = request.Year,
                Description = request.Description,
                StartsOn = request.StartsOn,
                EndsOn = request.EndsOn,
                CreatedByUserId = userId
            };

            _context.ScholarGenerations.Add(generation);
            await _context.SaveChangesAsync(cancellationToken);

            if (request.IsCurrent) await SetCurrentGenerationAsync(generation.Id, cancellationToken);

            _logger.LogInformation("Created generation {Name} ({Year}).", generation.Name, generation.Year);

            return (await GetGenerationsAsync(cancellationToken)).First(g => g.Id == generation.Id);
        }

        public async Task<ScholarGenerationDto> UpdateGenerationAsync(
            int id, UpsertGenerationRequest request, CancellationToken cancellationToken = default)
        {
            Validate(request);

            var generation = await _context.ScholarGenerations.FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
                ?? throw new NotFoundException("Generation", id.ToString());

            if (generation.Year != request.Year &&
                await _context.ScholarGenerations.AnyAsync(g => g.Id != id && g.Year == request.Year, cancellationToken))
            {
                throw new ConflictException($"A generation for {request.Year} already exists.");
            }

            generation.Name = request.Name.Trim();
            generation.Year = request.Year;
            generation.Description = request.Description;
            generation.StartsOn = request.StartsOn;
            generation.EndsOn = request.EndsOn;
            generation.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            if (request.IsCurrent && !generation.IsCurrent)
                await SetCurrentGenerationAsync(id, cancellationToken);

            return (await GetGenerationsAsync(cancellationToken)).First(g => g.Id == id);
        }

        public async Task SetCurrentGenerationAsync(int id, CancellationToken cancellationToken = default)
        {
            var generations = await _context.ScholarGenerations.ToListAsync(cancellationToken);

            var target = generations.FirstOrDefault(g => g.Id == id)
                ?? throw new NotFoundException("Generation", id.ToString());

            // Exactly one current generation. Clearing every other flag in the same
            // transaction is what keeps that true — a partial update would leave two.
            foreach (var generation in generations)
            {
                generation.IsCurrent = generation.Id == id;
                generation.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Generation {Name} is now current.", target.Name);
        }

        public async Task DeleteGenerationAsync(int id, CancellationToken cancellationToken = default)
        {
            var generation = await _context.ScholarGenerations
                .Include(g => g.Scholars)
                .FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
                ?? throw new NotFoundException("Generation", id.ToString());

            // Scholars survive and become ungrouped (SetNull on the FK), but silently
            // detaching a few hundred people is not something to do without warning.
            if (generation.Scholars.Count > 0)
            {
                throw new ValidationException(
                    $"{generation.Name} still has {generation.Scholars.Count} scholar(s). " +
                    "Move them to another generation first.");
            }

            _context.ScholarGenerations.Remove(generation);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // ── Overview ──────────────────────────────────────────────────────────

        public async Task<ScholarOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            var scholarRoleId = await _context.Roles
                .Where(r => r.Name == "User")
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            // "Scholars" means accounts in the User role. Staff accounts have their own roles
            // and must not appear in cohort counts or be swept up by promotion.
            var scholarIds = scholarRoleId is null
                ? new List<string>()
                : await _context.UserRoles
                    .Where(ur => ur.RoleId == scholarRoleId)
                    .Select(ur => ur.UserId)
                    .ToListAsync(cancellationToken);

            var scholars = await _context.Users
                .AsNoTracking()
                .Where(u => scholarIds.Contains(u.Id))
                .Select(u => new { u.ScholarStatus, u.IsActive, u.GenerationId })
                .ToListAsync(cancellationToken);

            var byStatus = Enum.GetValues<ScholarStatus>()
                .Select(status => new ScholarStatusCountDto
                {
                    Status = status,
                    Label = Describe(status),
                    Count = scholars.Count(s => s.ScholarStatus == status),
                    ActiveCount = scholars.Count(s => s.ScholarStatus == status && s.IsActive)
                })
                .ToList();

            return new ScholarOverviewDto
            {
                TotalScholars = scholars.Count,
                ByStatus = byStatus,
                Generations = await GetGenerationsAsync(cancellationToken),
                UngeneratedCount = scholars.Count(s => s.GenerationId is null),
                UnassignedStatusCount = scholars.Count(s => s.ScholarStatus == ScholarStatus.Unassigned)
            };
        }

        // ── Promotion ─────────────────────────────────────────────────────────

        public async Task<PromotionPreviewDto> PreviewPromotionAsync(
            PromotionRequest request, CancellationToken cancellationToken = default)
        {
            var (from, to) = Transition(request.Step);
            var candidates = await FindCandidatesAsync(request, from, cancellationToken);

            var generationName = request.GenerationId.HasValue
                ? (await _context.ScholarGenerations
                    .Where(g => g.Id == request.GenerationId)
                    .Select(g => g.Name)
                    .FirstOrDefaultAsync(cancellationToken))
                : null;

            var willDeactivate = request.Step == PromotionStep.SeniorsToAlumni && request.DeactivateAlumni;

            var summary = candidates.Count == 0
                ? $"No scholars are currently {Describe(from).ToLowerInvariant()}{(generationName is null ? "" : $" in {generationName}")}."
                : $"{candidates.Count} scholar(s) will move from {Describe(from)} to {Describe(to)}" +
                  $"{(generationName is null ? " across all generations" : $" in {generationName}")}." +
                  (willDeactivate
                      ? " They will also be deactivated, which stops them logging in and silences all email to them."
                      : string.Empty);

            return new PromotionPreviewDto
            {
                Step = request.Step,
                StepLabel = DescribeStep(request.Step),
                AffectedCount = candidates.Count,
                GenerationName = generationName,
                WillDeactivate = willDeactivate,
                Summary = summary,
                Samples = candidates
                    .Take(Math.Clamp(request.SampleSize, 1, 50))
                    .Select(u => new PromotionCandidateDto
                    {
                        UserId = u.Id,
                        DisplayName = $"{u.FirstName} {u.LastName}".Trim(),
                        Email = u.Email,
                        GenerationName = u.Generation?.Name,
                        CurrentStatus = u.ScholarStatus,
                        NewStatus = to,
                        IsActive = u.IsActive
                    })
                    .ToList()
            };
        }

        public async Task<PromotionResultDto> ApplyPromotionAsync(
            PromotionRequest request, string userId, string userName, CancellationToken cancellationToken = default)
        {
            var (from, to) = Transition(request.Step);
            var candidates = await FindCandidatesAsync(request, from, cancellationToken);

            if (candidates.Count == 0)
                throw new ValidationException($"No scholars are currently {Describe(from).ToLowerInvariant()}.");

            var deactivate = request.Step == PromotionStep.SeniorsToAlumni && request.DeactivateAlumni;

            var batch = new PromotionBatch
            {
                Step = request.Step,
                GenerationId = request.GenerationId,
                AffectedCount = candidates.Count,
                DeactivatedAlumni = deactivate,
                PerformedByUserId = userId,
                PerformedByName = userName
            };

            foreach (var scholar in candidates)
            {
                // Previous state captured per row, not derived from the step: a revert has to
                // restore what each account actually was, including ones that were already
                // inactive before the run and must stay that way.
                batch.Entries.Add(new PromotionBatchEntry
                {
                    UserId = scholar.Id,
                    UserDisplayName = $"{scholar.FirstName} {scholar.LastName}".Trim(),
                    UserEmail = scholar.Email,
                    PreviousStatus = scholar.ScholarStatus,
                    NewStatus = to,
                    PreviousTitle = scholar.Title,
                    PreviousIsActive = scholar.IsActive
                });

                scholar.ScholarStatus = to;
                scholar.Title = Describe(to);
                scholar.UpdatedAt = DateTime.UtcNow;

                if (deactivate) scholar.IsActive = false;
            }

            _context.PromotionBatches.Add(batch);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Promotion {Step} moved {Count} scholar(s) (batch {BatchId}, by {User}).",
                request.Step, candidates.Count, batch.Id, userName);

            return new PromotionResultDto
            {
                BatchId = batch.Id,
                AffectedCount = candidates.Count,
                Message = $"{candidates.Count} scholar(s) moved from {Describe(from)} to {Describe(to)}."
            };
        }

        public async Task<List<PromotionBatchDto>> GetPromotionHistoryAsync(
            int limit = 25, CancellationToken cancellationToken = default)
        {
            return await _context.PromotionBatches
                .AsNoTracking()
                .Include(b => b.Generation)
                .OrderByDescending(b => b.PerformedAt)
                .Take(Math.Clamp(limit, 1, 200))
                .Select(b => new PromotionBatchDto
                {
                    Id = b.Id,
                    Step = b.Step,
                    StepLabel = b.Step == PromotionStep.SeniorsToAlumni ? "Seniors → Alumni" : "Juniors → Seniors",
                    GenerationName = b.Generation != null ? b.Generation.Name : null,
                    AffectedCount = b.AffectedCount,
                    DeactivatedAlumni = b.DeactivatedAlumni,
                    PerformedByName = b.PerformedByName,
                    PerformedAt = b.PerformedAt,
                    IsReverted = b.RevertedAt != null,
                    RevertedAt = b.RevertedAt
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<PromotionResultDto> RevertPromotionAsync(
            int batchId, string userId, CancellationToken cancellationToken = default)
        {
            var batch = await _context.PromotionBatches
                .Include(b => b.Entries)
                .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
                ?? throw new NotFoundException("Promotion batch", batchId.ToString());

            if (batch.IsReverted)
                throw new ValidationException("This promotion has already been reverted.");

            var userIds = batch.Entries.Select(e => e.UserId).ToList();
            var scholars = await _context.Users
                .Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, cancellationToken);

            var restored = 0;

            foreach (var entry in batch.Entries)
            {
                if (!scholars.TryGetValue(entry.UserId, out var scholar)) continue;

                // Only revert accounts still sitting where the promotion left them. If someone
                // has been moved again since, their newer state is the intended one and
                // stamping over it would undo a decision nobody asked to undo.
                if (scholar.ScholarStatus != entry.NewStatus) continue;

                scholar.ScholarStatus = entry.PreviousStatus;
                scholar.Title = entry.PreviousTitle;
                scholar.IsActive = entry.PreviousIsActive;
                scholar.UpdatedAt = DateTime.UtcNow;
                restored++;
            }

            batch.RevertedAt = DateTime.UtcNow;
            batch.RevertedByUserId = userId;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Reverted promotion batch {BatchId}: {Count} scholar(s) restored.", batchId, restored);

            return new PromotionResultDto
            {
                BatchId = batchId,
                AffectedCount = restored,
                Message = restored == batch.Entries.Count
                    ? $"{restored} scholar(s) restored."
                    : $"{restored} of {batch.Entries.Count} restored — the rest have been changed since and were left alone."
            };
        }

        public async Task<int> SetStatusAsync(
            List<string> userIds, ScholarStatus status, int? generationId, CancellationToken cancellationToken = default)
        {
            if (userIds.Count == 0) return 0;

            var scholars = await _context.Users
                .Where(u => userIds.Contains(u.Id))
                .ToListAsync(cancellationToken);

            foreach (var scholar in scholars)
            {
                scholar.ScholarStatus = status;
                scholar.Title = Describe(status);
                if (generationId.HasValue) scholar.GenerationId = generationId;
                scholar.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return scholars.Count;
        }

        // ── Bulk intake ───────────────────────────────────────────────────────

        public async Task<ScholarImportResultDto> ImportScholarsAsync(
            Stream fileStream,
            string fileName,
            ScholarImportOptions options,
            string? userId,
            CancellationToken cancellationToken = default)
        {
            var (headers, rows) = SpreadsheetReader.Read(fileStream, fileName);

            var result = new ScholarImportResultDto
            {
                WasDryRun = options.DryRun,
                FileName = fileName,
                TotalRows = rows.Count,
                DetectedColumns = headers
            };

            var map = SpreadsheetReader.BuildColumnMap(headers, ColumnAliases);

            if (!map.ContainsKey("email"))
            {
                result.FailedCount = rows.Count;
                result.Issues.Add(new ScholarImportRowIssueDto
                {
                    Outcome = "Failed",
                    Message = $"The file needs an email column. Found: {string.Join(", ", headers)}."
                });
                return result;
            }

            var generation = options.GenerationId.HasValue
                ? await _context.ScholarGenerations.FirstOrDefaultAsync(g => g.Id == options.GenerationId, cancellationToken)
                : await _context.ScholarGenerations.FirstOrDefaultAsync(g => g.IsCurrent, cancellationToken);

            result.GenerationName = generation?.Name;

            // Existing addresses fetched once. A per-row lookup over a few hundred rows is
            // what turns a two-second import into a minute of round trips.
            var existing = await _context.Users
                .Where(u => u.Email != null)
                .Select(u => u.Email!.ToLower())
                .ToListAsync(cancellationToken);

            var known = new HashSet<string>(existing, StringComparer.Ordinal);
            var seenInFile = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < rows.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = rows[i];
                var rowNumber = i + 2;

                var email = SpreadsheetReader.Value(row, map, "email")?.Trim();
                var normalized = email?.ToLowerInvariant();

                var (firstName, lastName) = ResolveName(row, map);

                if (string.IsNullOrWhiteSpace(normalized))
                {
                    result.SkippedCount++;
                    continue;
                }

                if (!LooksLikeEmail(normalized))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, $"{firstName} {lastName}".Trim(), email, "Failed", "Email address is not valid.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(firstName))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, null, email, "Failed", "No name column could be read for this row.");
                    continue;
                }

                if (known.Contains(normalized))
                {
                    result.SkippedCount++;
                    AddIssue(result, rowNumber, $"{firstName} {lastName}".Trim(), email, "Skipped", "An account with this email already exists.");
                    continue;
                }

                if (!seenInFile.Add(normalized))
                {
                    result.SkippedCount++;
                    AddIssue(result, rowNumber, $"{firstName} {lastName}".Trim(), email, "Skipped", "Duplicate address within this file.");
                    continue;
                }

                var password = GeneratePassword();

                if (options.DryRun)
                {
                    result.CreatedCount++;
                    continue;
                }

                var user = new User
                {
                    UserName = email,
                    Email = email,
                    FirstName = firstName,
                    LastName = lastName,
                    Title = Describe(options.Status),
                    ScholarStatus = options.Status,
                    GenerationId = generation?.Id,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsActive = true,
                    EmailConfirmed = false,
                    MustChangePassword = true
                };

                var created = await _userManager.CreateAsync(user, password);

                if (!created.Succeeded)
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, $"{firstName} {lastName}".Trim(), email, "Failed",
                        string.Join("; ", created.Errors.Select(e => e.Description)));
                    continue;
                }

                await _userManager.AddToRoleAsync(user, "User");

                known.Add(normalized);
                result.CreatedCount++;

                result.Credentials.Add(new CreatedScholarCredentialDto
                {
                    FirstName = firstName,
                    LastName = lastName ?? string.Empty,
                    Email = email!,
                    TemporaryPassword = password
                });
            }

            if (!options.DryRun && options.ArchiveCredentials && result.Credentials.Count > 0)
            {
                var csv = BuildCredentialCsv(result.Credentials);
                var path = $"/scholar-intake-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

                var upload = await _dropbox.TryUploadTextAsync(path, csv, cancellationToken);
                result.CredentialsArchived = upload.Success;
            }

            _logger.LogInformation(
                "Scholar import ({Mode}) of {File}: {Created} created, {Skipped} skipped, {Failed} failed.",
                options.DryRun ? "dry run" : "committed", fileName,
                result.CreatedCount, result.SkippedCount, result.FailedCount);

            return result;
        }

        public ExportTable BuildImportTemplate() => new()
        {
            Name = "Scholars",
            Headers = new List<string> { "First name", "Last name", "Email" },
            Rows = new List<List<object?>>
            {
                new() { "Amina", "Hodzic", "amina.hodzic@example.ba" },
                new() { "Tarik", "Begic", "tarik.begic@example.ba" }
            }
        };

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Candidate scholars for a promotion step.
        ///
        /// Restricted to accounts in the User role so staff are never swept up, and to
        /// scholars with a cohort when one is specified.
        /// </summary>
        private async Task<List<User>> FindCandidatesAsync(
            PromotionRequest request, ScholarStatus from, CancellationToken cancellationToken)
        {
            var scholarRoleId = await _context.Roles
                .Where(r => r.Name == "User")
                .Select(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (scholarRoleId is null) return new List<User>();

            var scholarIds = await _context.UserRoles
                .Where(ur => ur.RoleId == scholarRoleId)
                .Select(ur => ur.UserId)
                .ToListAsync(cancellationToken);

            var query = _context.Users
                .Include(u => u.Generation)
                .Where(u => scholarIds.Contains(u.Id) && u.ScholarStatus == from);

            if (request.GenerationId.HasValue)
                query = query.Where(u => u.GenerationId == request.GenerationId);

            return await query
                .OrderBy(u => u.LastName)
                .ThenBy(u => u.FirstName)
                .ToListAsync(cancellationToken);
        }

        private static (ScholarStatus From, ScholarStatus To) Transition(PromotionStep step) => step switch
        {
            PromotionStep.SeniorsToAlumni => (ScholarStatus.Senior, ScholarStatus.Alumni),
            PromotionStep.JuniorsToSeniors => (ScholarStatus.Junior, ScholarStatus.Senior),
            _ => throw new ValidationException($"Unsupported promotion step: {step}")
        };

        private static string Describe(ScholarStatus status) => status switch
        {
            ScholarStatus.Junior => "Junior",
            ScholarStatus.Senior => "Senior",
            ScholarStatus.Alumni => "Alumni",
            ScholarStatus.Withdrawn => "Withdrawn",
            _ => "Unassigned"
        };

        private static string DescribeStep(PromotionStep step) =>
            step == PromotionStep.SeniorsToAlumni ? "Seniors → Alumni" : "Juniors → Seniors";

        private static void Validate(UpsertGenerationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new ValidationException("A generation needs a name.");

            if (request.Year is < 2000 or > 2200)
                throw new ValidationException("Year must be a realistic four-digit year.");

            if (request.StartsOn.HasValue && request.EndsOn.HasValue && request.EndsOn < request.StartsOn)
                throw new ValidationException("The end date cannot be before the start date.");
        }

        private static (string? First, string? Last) ResolveName(string?[] row, Dictionary<string, int> map)
        {
            var first = SpreadsheetReader.Value(row, map, "firstname")?.Trim();
            var last = SpreadsheetReader.Value(row, map, "lastname")?.Trim();

            if (!string.IsNullOrWhiteSpace(first)) return (first, last);

            // Many lists carry a single "Name" column. Split on the last space so
            // double-barrelled given names stay with the first name.
            var full = SpreadsheetReader.Value(row, map, "fullname")?.Trim();
            if (string.IsNullOrWhiteSpace(full)) return (null, null);

            var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => (null, null),
                1 => (parts[0], null),
                _ => (string.Join(' ', parts[..^1]), parts[^1])
            };
        }

        /// <summary>
        /// Temporary password meeting the configured Identity policy (upper, lower, digit,
        /// symbol, 8+). Uses a cryptographic RNG rather than System.Random: these are real
        /// credentials for real accounts, and Random is seeded predictably enough that a
        /// batch generated in one run is guessable from any one of its outputs.
        /// </summary>
        private static string GeneratePassword()
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // no I or O
            const string lower = "abcdefghijkmnpqrstuvwxyz";   // no l
            const string digits = "23456789";                  // no 0 or 1
            const string symbols = "!@#$%*?";

            var all = upper + lower + digits + symbols;

            var chars = new List<char>
            {
                Pick(upper), Pick(lower), Pick(digits), Pick(symbols)
            };

            while (chars.Count < 12) chars.Add(Pick(all));

            // Shuffle so the guaranteed character classes aren't always in the same positions.
            for (var i = chars.Count - 1; i > 0; i--)
            {
                var j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars.ToArray());

            static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
        }

        private static string BuildCredentialCsv(List<CreatedScholarCredentialDto> credentials)
        {
            var lines = new List<string> { "First name,Last name,Email,Temporary password" };

            lines.AddRange(credentials.Select(c =>
                $"{Escape(c.FirstName)},{Escape(c.LastName)},{Escape(c.Email)},{Escape(c.TemporaryPassword)}"));

            return string.Join("\n", lines);

            static string Escape(string value) =>
                value.Contains(',') || value.Contains('"')
                    ? $"\"{value.Replace("\"", "\"\"")}\""
                    : value;
        }

        private static bool LooksLikeEmail(string email)
        {
            var at = email.IndexOf('@');
            return at > 0
                && at < email.Length - 1
                && email.IndexOf('@', at + 1) < 0
                && email.LastIndexOf('.') > at
                && !email.Contains(' ');
        }

        private static void AddIssue(
            ScholarImportResultDto result, int rowNumber, string? name, string? email, string outcome, string message)
        {
            if (result.Issues.Count >= MaxReportedIssues) return;

            result.Issues.Add(new ScholarImportRowIssueDto
            {
                RowNumber = rowNumber,
                Name = name,
                Email = email,
                Outcome = outcome,
                Message = message
            });
        }
    }
}
