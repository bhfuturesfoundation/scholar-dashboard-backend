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
            ["status"] = new[] { "status", "scholar status", "category", "kategorija" },
            ["generation"] = new[] { "generation", "generacija", "cohort", "generation name" },
            ["mentor1"] = new[]
            {
                "mentor", "mentor email", "mentor 1 email", "mentor1 email", "first mentor email",
                "email mentora", "email mentora 1"
            },
            ["mentor2"] = new[] { "mentor 2 email", "mentor2 email", "second mentor email", "email mentora 2" },
            ["goal1"] = new[] { "personal goal", "personal development goal", "licni cilj", "goal personal" },
            ["rating1"] = new[] { "personal satisfaction", "personal goal satisfaction", "zadovoljstvo licni cilj" },
            ["goal2"] = new[] { "professional goal", "professional development goal", "profesionalni cilj", "goal professional" },
            ["rating2"] = new[] { "professional satisfaction", "professional goal satisfaction", "zadovoljstvo profesionalni cilj" },
            ["goal3"] = new[] { "teamwork goal", "leadership goal", "teamwork leadership goal", "timski cilj", "goal teamwork" },
            ["rating3"] = new[] { "teamwork satisfaction", "teamwork goal satisfaction", "leadership satisfaction", "zadovoljstvo timski cilj" },
            ["goal4"] = new[] { "knowledge goal", "knowledge skill goal", "cilj znanja", "goal knowledge" },
            ["rating4"] = new[] { "knowledge satisfaction", "knowledge goal satisfaction", "zadovoljstvo cilj znanja" },
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

            var generationsByName = (await _context.ScholarGenerations.ToListAsync(cancellationToken))
                .GroupBy(g => g.Name.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            // Every account fetched once, keyed by email. A per-row lookup over a few hundred
            // rows is what turns a two-second import into a minute of round trips.
            var accounts = await _context.Users
                .Where(u => u.Email != null)
                .ToDictionaryAsync(u => u.Email!.ToLower(), cancellationToken);

            var mentorRoleId = await _context.Roles.Where(r => r.Name == "Mentor").Select(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            var mentorIds = (await _context.UserRoles.Where(ur => ur.RoleId == mentorRoleId).Select(ur => ur.UserId).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var scholarRoleId = await _context.Roles.Where(r => r.Name == "User").Select(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            var scholarIds = (await _context.UserRoles.Where(ur => ur.RoleId == scholarRoleId).Select(ur => ur.UserId).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var goalQuestions = await _context.Questions
                .Where(q => q.IsSkill && q.Active && q.SkillType != null)
                .ToDictionaryAsync(q => (int)q.SkillType!, q => q.QuestionId, cancellationToken);

            var seenInFile = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < rows.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = rows[i];
                var rowNumber = i + 2;

                var email = SpreadsheetReader.Value(row, map, "email")?.Trim();
                var normalized = email?.ToLowerInvariant();

                var (firstName, lastName) = ResolveName(row, map);
                var label = $"{firstName} {lastName}".Trim();

                if (string.IsNullOrWhiteSpace(normalized))
                {
                    result.SkippedCount++;
                    continue;
                }

                if (!LooksLikeEmail(normalized))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, label, email, "Failed", "Email address is not valid.");
                    continue;
                }

                if (!seenInFile.Add(normalized))
                {
                    result.SkippedCount++;
                    AddIssue(result, rowNumber, label, email, "Skipped", "Duplicate address within this file.");
                    continue;
                }

                // Per-row status and generation override the options chosen on screen.
                var statusText = SpreadsheetReader.Value(row, map, "status")?.Trim();
                var status = options.Status;
                if (!string.IsNullOrEmpty(statusText))
                {
                    if (!Enum.TryParse<ScholarStatus>(statusText, true, out status) || !Enum.IsDefined(status))
                    {
                        result.FailedCount++;
                        AddIssue(result, rowNumber, label, email, "Failed", $"Unknown status \"{statusText}\". Use Junior, Senior or Alumni.");
                        continue;
                    }
                }

                var rowGeneration = generation;
                var generationText = SpreadsheetReader.Value(row, map, "generation")?.Trim();
                if (!string.IsNullOrEmpty(generationText))
                {
                    if (!generationsByName.TryGetValue(generationText.ToLowerInvariant(), out rowGeneration))
                    {
                        result.FailedCount++;
                        AddIssue(result, rowNumber, label, email, "Failed", $"No generation is called \"{generationText}\". Create it first.");
                        continue;
                    }
                }

                var isExisting = accounts.TryGetValue(normalized, out var user);

                if (isExisting && !options.UpdateExisting)
                {
                    result.SkippedCount++;
                    AddIssue(result, rowNumber, label, email, "Skipped",
                        "An account with this email already exists. Tick \"Update existing scholars\" to update their goals and mentors.");
                    continue;
                }

                if (isExisting && !scholarIds.Contains(user!.Id))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, label, email, "Failed", "This email belongs to an account that is not a scholar.");
                    continue;
                }

                if (!isExisting && string.IsNullOrWhiteSpace(firstName))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, null, email, "Failed", "No name column could be read for this row.");
                    continue;
                }

                // Mentors are checked before anything is written, so a bad address is reported
                // without half-importing the row.
                var mentorProblem = (string?)null;
                var mentors = new List<User>();
                foreach (var column in new[] { "mentor1", "mentor2" })
                {
                    var mentorEmail = SpreadsheetReader.Value(row, map, column)?.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(mentorEmail)) continue;

                    if (!accounts.TryGetValue(mentorEmail, out var mentor) || !mentorIds.Contains(mentor.Id))
                    {
                        mentorProblem = $"{mentorEmail} is not a mentor account.";
                        break;
                    }

                    if (mentors.All(m => m.Id != mentor.Id)) mentors.Add(mentor);
                }

                if (mentorProblem is not null)
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, label, email, "Failed", mentorProblem);
                    continue;
                }

                var goals = Enumerable.Range(1, 4)
                    .Select(slot => (
                        Slot: slot,
                        Answer: SpreadsheetReader.Value(row, map, $"goal{slot}")?.Trim(),
                        Rating: SpreadsheetReader.Value(row, map, $"rating{slot}")?.Trim()))
                    .Where(g => !string.IsNullOrEmpty(g.Answer))
                    .ToList();

                var missingQuestion = goals.FirstOrDefault(g => !goalQuestions.ContainsKey(g.Slot));
                if (missingQuestion.Answer is not null)
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, label, email, "Failed",
                        "There is no active journal question for one of these goal areas. Set the skill type on the questions first.");
                    continue;
                }

                if (options.DryRun)
                {
                    if (isExisting) result.UpdatedCount++;
                    else result.CreatedCount++;
                    result.GoalsSetCount += goals.Count;
                    result.MentorsAssignedCount += mentors.Count;
                    continue;
                }

                if (!isExisting)
                {
                    var password = TemporaryPassword.Generate();

                    user = new User
                    {
                        UserName = email,
                        Email = email,
                        FirstName = firstName,
                        LastName = lastName,
                        Title = Describe(status),
                        ScholarStatus = status,
                        GenerationId = rowGeneration?.Id,
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
                        AddIssue(result, rowNumber, label, email, "Failed",
                            string.Join("; ", created.Errors.Select(e => e.Description)));
                        continue;
                    }

                    await _userManager.AddToRoleAsync(user, "User");

                    accounts[normalized] = user;
                    result.CreatedCount++;

                    result.Credentials.Add(new CreatedScholarCredentialDto
                    {
                        FirstName = firstName!,
                        LastName = lastName ?? string.Empty,
                        Email = email!,
                        TemporaryPassword = password
                    });
                }
                else
                {
                    // Only what the sheet actually says: an empty status or generation cell
                    // leaves the scholar's current value alone.
                    if (!string.IsNullOrEmpty(statusText))
                    {
                        user!.ScholarStatus = status;
                        user.Title = Describe(status);
                    }

                    if (!string.IsNullOrEmpty(generationText)) user!.GenerationId = rowGeneration?.Id;

                    user!.UpdatedAt = DateTime.UtcNow;
                    result.UpdatedCount++;
                }

                result.GoalsSetCount += await SetGoalsAsync(user!.Id, goals, goalQuestions, cancellationToken);
                result.MentorsAssignedCount += await AddMentorsAsync(user.Id, mentors, cancellationToken);

                await _context.SaveChangesAsync(cancellationToken);
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

        /// <summary>
        /// Everything a scholar can be imported with. Only Email and the names are needed for a
        /// new account; every other column is optional and an empty cell is ignored.
        /// </summary>
        public ExportTable BuildImportTemplate() => new()
        {
            Name = "Scholars",
            Headers = new List<string>
            {
                "First name", "Last name", "Email", "Status", "Generation",
                "Mentor 1 email", "Mentor 2 email",
                "Personal goal", "Personal satisfaction",
                "Professional goal", "Professional satisfaction",
                "Teamwork goal", "Teamwork satisfaction",
                "Knowledge goal", "Knowledge satisfaction"
            },
            Rows = new List<List<object?>>
            {
                new()
                {
                    "Amina", "Hodzic", "amina.hodzic@example.ba", "Junior", "",
                    "mentor.one@example.ba", "",
                    "Speak up more in team meetings", "2",
                    "Finish an internship application", "3",
                    "Lead one volunteering project", "2",
                    "Complete a data analysis course", "3"
                },
                new()
                {
                    "Tarik", "Begic", "tarik.begic@example.ba", "Senior", "",
                    "mentor.one@example.ba", "mentor.two@example.ba",
                    "", "", "", "", "", "", "", ""
                }
            }
        };

        /// <summary>
        /// Writes a scholar's goals: each given area gets a new active goal and the previous one
        /// in that area is kept but switched off, the same as when a scholar edits it.
        /// </summary>
        private async Task<int> SetGoalsAsync(
            string scholarId,
            List<(int Slot, string? Answer, string? Rating)> goals,
            Dictionary<int, int> goalQuestions,
            CancellationToken cancellationToken)
        {
            if (goals.Count == 0) return 0;

            var slots = goals.Select(g => g.Slot).ToList();
            var current = await _context.Skills
                .Where(s => s.ScholarId == scholarId && s.Active && slots.Contains(s.Slot))
                .ToListAsync(cancellationToken);

            foreach (var previous in current) previous.Active = false;

            foreach (var goal in goals)
            {
                _context.Skills.Add(new Skill
                {
                    ScholarId = scholarId,
                    QuestionId = goalQuestions[goal.Slot],
                    Slot = goal.Slot,
                    SkillAnswer = goal.Answer!,
                    SatisfactionRating = string.IsNullOrEmpty(goal.Rating) ? null : goal.Rating,
                    Active = true
                });
            }

            return goals.Count;
        }

        /// <summary>Adds mentors the scholar doesn't have yet, up to the limit. Never removes any.</summary>
        private async Task<int> AddMentorsAsync(string scholarId, List<User> mentors, CancellationToken cancellationToken)
        {
            if (mentors.Count == 0) return 0;

            var current = await _context.MentorAssignments
                .Where(a => a.ScholarId == scholarId)
                .Select(a => a.MentorId)
                .ToListAsync(cancellationToken);

            var added = 0;
            foreach (var mentor in mentors)
            {
                if (current.Contains(mentor.Id) || current.Count >= MentorAssignment.MaxMentorsPerScholar) continue;

                _context.MentorAssignments.Add(new MentorAssignment
                {
                    ScholarId = scholarId,
                    MentorId = mentor.Id,
                    AssignedAt = DateTime.UtcNow
                });
                current.Add(mentor.Id);
                added++;
            }

            return added;
        }

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

        private static string BuildCredentialCsv(List<CreatedScholarCredentialDto> credentials)
        {
            var lines = new List<string> { "First name,Last name,Email,Temporary password" };

            lines.AddRange(credentials.Select(c =>
                $"{Escape(c.FirstName)},{Escape(c.LastName)},{Escape(c.Email)},{Escape(c.TemporaryPassword)}"));

            return string.Join("\n", lines);

            // Formula guard included; passwords never start with a symbol, so it can't alter one.
            static string Escape(string value) => TabularExporter.CsvEscape(value);
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
