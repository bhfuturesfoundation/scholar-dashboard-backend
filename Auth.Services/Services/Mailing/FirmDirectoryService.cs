using Auth.Models.Data;
using Auth.Models.DTOs.Mailing;
using Auth.Models.Entities.Mailing;
using Auth.Models.Enums.Mailing;
using Auth.Models.Exceptions;
using Auth.Models.Request.Mailing;
using Auth.Models.Response;
using Auth.Services.Interfaces.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Mailing
{
    public class FirmDirectoryService : IFirmDirectoryService
    {
        private readonly ApplicationDbContext _context;
        private readonly IContactNameExtractor _nameExtractor;
        private readonly IFirmCategorizer _categorizer;
        private readonly ILogger<FirmDirectoryService> _logger;

        public FirmDirectoryService(
            ApplicationDbContext context,
            IContactNameExtractor nameExtractor,
            IFirmCategorizer categorizer,
            ILogger<FirmDirectoryService> logger)
        {
            _context = context;
            _nameExtractor = nameExtractor;
            _categorizer = categorizer;
            _logger = logger;
        }

        public async Task<PagedResult<FirmDto>> SearchAsync(FirmQuery query, CancellationToken cancellationToken = default)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 200);

            var firms = _context.Firms
                .AsNoTracking()
                .Include(f => f.FirmType)!
                    .ThenInclude(t => t!.FirmGroup)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim().ToLowerInvariant();
                firms = firms.Where(f =>
                    f.Name.ToLower().Contains(term) ||
                    (f.LegalName != null && f.LegalName.ToLower().Contains(term)) ||
                    (f.NormalizedEmail != null && f.NormalizedEmail.Contains(term)) ||
                    (f.ContactPersonName != null && f.ContactPersonName.ToLower().Contains(term)) ||
                    (f.City != null && f.City.ToLower().Contains(term)));
            }

            if (query.FirmTypeId.HasValue)
                firms = firms.Where(f => f.FirmTypeId == query.FirmTypeId);

            if (query.FirmGroupId.HasValue)
                firms = firms.Where(f => f.FirmType != null && f.FirmType.FirmGroupId == query.FirmGroupId);

            if (query.Status.HasValue)
                firms = firms.Where(f => f.Status == query.Status);

            if (query.HasContactName.HasValue)
            {
                firms = query.HasContactName.Value
                    ? firms.Where(f => f.ContactPersonName != null && f.ContactNameConfidence >= NameConfidence.Medium)
                    : firms.Where(f => f.ContactPersonName == null || f.ContactNameConfidence < NameConfidence.Medium);
            }

            var total = await firms.CountAsync(cancellationToken);

            var items = await firms
                .OrderBy(f => f.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<FirmDto>
            {
                Items = items.Select(Map).ToList(),
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<FirmDto?> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var firm = await _context.Firms
                .AsNoTracking()
                .Include(f => f.FirmType)!
                    .ThenInclude(t => t!.FirmGroup)
                .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

            return firm is null ? null : Map(firm);
        }

        public async Task<FirmDto> CreateAsync(
            UpsertFirmRequest request, string? userId, CancellationToken cancellationToken = default)
        {
            var normalizedEmail = TextNormalizer.NormalizeEmail(request.Email);

            if (normalizedEmail is not null)
            {
                var duplicate = await _context.Firms
                    .AnyAsync(f => f.NormalizedEmail == normalizedEmail, cancellationToken);

                if (duplicate)
                    throw new ConflictException($"A firm with the email {request.Email} already exists.");
            }

            var firm = new Firm
            {
                CreatedByUserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            ApplyRequest(firm, request, normalizedEmail);

            // A firm typed in by hand with a contact name means a person decided that name.
            if (!string.IsNullOrWhiteSpace(request.ContactPersonName))
            {
                firm.ContactNameSource = ContactNameSource.Manual;
                firm.ContactNameConfidence = NameConfidence.High;
            }

            _context.Firms.Add(firm);
            await _context.SaveChangesAsync(cancellationToken);

            return (await GetAsync(firm.Id, cancellationToken))!;
        }

        public async Task<FirmDto> UpdateAsync(
            int id, UpsertFirmRequest request, CancellationToken cancellationToken = default)
        {
            var firm = await _context.Firms.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new NotFoundException("Firm", id.ToString());

            var normalizedEmail = TextNormalizer.NormalizeEmail(request.Email);

            if (normalizedEmail is not null && normalizedEmail != firm.NormalizedEmail)
            {
                var duplicate = await _context.Firms
                    .AnyAsync(f => f.Id != id && f.NormalizedEmail == normalizedEmail, cancellationToken);

                if (duplicate)
                    throw new ConflictException($"Another firm already uses the email {request.Email}.");
            }

            var nameChanged = !string.Equals(firm.ContactPersonName, request.ContactPersonName, StringComparison.Ordinal);

            ApplyRequest(firm, request, normalizedEmail);

            // Editing the name through the UI is a human decision — record it as Manual so
            // later bulk detection runs leave it alone.
            if (nameChanged && !string.IsNullOrWhiteSpace(request.ContactPersonName))
            {
                firm.ContactNameSource = ContactNameSource.Manual;
                firm.ContactNameConfidence = NameConfidence.High;
            }
            else if (string.IsNullOrWhiteSpace(request.ContactPersonName))
            {
                firm.ContactNameSource = ContactNameSource.None;
                firm.ContactNameConfidence = NameConfidence.None;
            }

            firm.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            return (await GetAsync(firm.Id, cancellationToken))!;
        }

        public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            var firm = await _context.Firms.FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
                ?? throw new NotFoundException("Firm", id.ToString());

            // Delivery history references firms with Restrict, so a firm that has been mailed
            // cannot be hard-deleted without shredding the record of what was sent to it.
            // Suppress it instead, which is what "remove from the list" actually means here.
            var hasHistory = await _context.MailingCampaignRecipients
                .AnyAsync(r => r.FirmId == id, cancellationToken);

            if (hasHistory)
            {
                firm.Status = FirmStatus.DoNotContact;
                firm.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Firm {Id} has delivery history — marked do-not-contact instead of deleted.", id);
                return;
            }

            _context.Firms.Remove(firm);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task<List<NameDetectionResultDto>> DetectNamesAsync(
            DetectNamesRequest request, CancellationToken cancellationToken = default)
        {
            var query = _context.Firms.AsNoTracking().AsQueryable();

            if (request.FirmIds.Count > 0)
            {
                query = query.Where(f => request.FirmIds.Contains(f.Id));
            }
            else if (request.FirmTypeId.HasValue)
            {
                query = query.Where(f => f.FirmTypeId == request.FirmTypeId);
            }

            if (!request.IncludeFirmsWithNames)
                query = query.Where(f => f.ContactPersonName == null || f.ContactPersonName == "");

            var firms = await query.OrderBy(f => f.Name).Take(2000).ToListAsync(cancellationToken);

            var results = new List<NameDetectionResultDto>(firms.Count);

            foreach (var firm in firms)
            {
                var extracted = _nameExtractor.Extract(firm.Email, firm.Name);
                var isManual = firm.ContactNameSource == ContactNameSource.Manual;

                results.Add(new NameDetectionResultDto
                {
                    FirmId = firm.Id,
                    FirmName = firm.Name,
                    Email = firm.Email,
                    CurrentName = firm.ContactPersonName,
                    SuggestedName = extracted.FullName,
                    Confidence = extracted.Confidence,
                    Source = extracted.Source,
                    Reason = isManual
                        ? "This name was set by hand — automatic detection will not overwrite it."
                        : extracted.Reason,
                    IsManuallySet = isManual,

                    // Only usable suggestions are ticked by default, and never over a name a
                    // human already chose. Everything else needs a deliberate click.
                    SelectedByDefault = extracted.IsUsable && !isManual
                });
            }

            _logger.LogInformation(
                "Name detection over {Count} firms: {Usable} usable suggestions.",
                results.Count, results.Count(r => r.SelectedByDefault));

            return results;
        }

        public async Task<int> ApplyNamesAsync(
            ApplyNamesRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Items.Count == 0) return 0;

            var ids = request.Items.Select(i => i.FirmId).ToList();
            var firms = await _context.Firms
                .Where(f => ids.Contains(f.Id))
                .ToDictionaryAsync(f => f.Id, cancellationToken);

            var updated = 0;

            foreach (var item in request.Items)
            {
                if (!firms.TryGetValue(item.FirmId, out var firm)) continue;

                if (string.IsNullOrWhiteSpace(item.ContactPersonName))
                {
                    firm.ContactPersonName = null;
                    firm.ContactNameSource = ContactNameSource.None;
                    firm.ContactNameConfidence = NameConfidence.None;
                }
                else
                {
                    firm.ContactPersonName = item.ContactPersonName.Trim();

                    // An operator who edited the suggestion has effectively vouched for it,
                    // so it becomes Manual/High and is protected from future detection runs.
                    firm.ContactNameSource = item.WasEdited ? ContactNameSource.Manual : item.Source;
                    firm.ContactNameConfidence = item.WasEdited ? NameConfidence.High : item.Confidence;
                }

                firm.UpdatedAt = DateTime.UtcNow;
                updated++;
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Applied contact names to {Count} firms.", updated);
            return updated;
        }

        public async Task<int> BulkCategorizeAsync(
            BulkCategorizeRequest request, CancellationToken cancellationToken = default)
        {
            var types = await _context.FirmTypes.AsNoTracking().ToListAsync(cancellationToken);
            if (types.Count == 0) return 0;

            var query = _context.Firms.AsQueryable();

            if (request.FirmIds.Count > 0)
                query = query.Where(f => request.FirmIds.Contains(f.Id));

            if (!request.OverwriteExisting)
                query = query.Where(f => f.FirmTypeId == null);

            var firms = await query.Take(5000).ToListAsync(cancellationToken);

            var categorized = 0;

            foreach (var firm in firms)
            {
                var suggestion = _categorizer.Suggest(firm.Name, firm.Website, firm.Email, types);

                if (!suggestion.HasSuggestion) continue;
                if (request.ConfidentOnly && !suggestion.IsConfident) continue;

                firm.FirmTypeId = suggestion.FirmTypeId;
                firm.UpdatedAt = DateTime.UtcNow;
                categorized++;
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Auto-categorised {Count} of {Total} firms.", categorized, firms.Count);
            return categorized;
        }

        public async Task<int> BulkSetStatusAsync(
            List<int> firmIds, FirmStatus status, CancellationToken cancellationToken = default)
        {
            if (firmIds.Count == 0) return 0;

            var firms = await _context.Firms
                .Where(f => firmIds.Contains(f.Id))
                .ToListAsync(cancellationToken);

            foreach (var firm in firms)
            {
                firm.Status = status;
                firm.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return firms.Count;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void ApplyRequest(Firm firm, UpsertFirmRequest request, string? normalizedEmail)
        {
            firm.Name = request.Name.Trim();
            firm.LegalName = request.LegalName?.Trim();
            firm.FirmTypeId = request.FirmTypeId;
            firm.Email = request.Email?.Trim();
            firm.NormalizedEmail = normalizedEmail;
            firm.Website = request.Website?.Trim();
            firm.Phone = request.Phone?.Trim();
            firm.Address = request.Address?.Trim();
            firm.City = request.City?.Trim();
            firm.Country = request.Country?.Trim();
            firm.ContactPersonName = string.IsNullOrWhiteSpace(request.ContactPersonName)
                ? null
                : request.ContactPersonName.Trim();
            firm.ContactPersonRole = request.ContactPersonRole?.Trim();
            firm.Status = request.Status;
            firm.Notes = request.Notes;
        }

        private static FirmDto Map(Firm firm) => new()
        {
            Id = firm.Id,
            Name = firm.Name,
            LegalName = firm.LegalName,
            FirmTypeId = firm.FirmTypeId,
            FirmTypeName = firm.FirmType?.Name,
            FirmGroupName = firm.FirmType?.FirmGroup?.Name,
            Email = firm.Email,
            Website = firm.Website,
            Phone = firm.Phone,
            City = firm.City,
            Country = firm.Country,
            ContactPersonName = firm.ContactPersonName,
            ContactPersonRole = firm.ContactPersonRole,
            ContactNameSource = firm.ContactNameSource,
            ContactNameConfidence = firm.ContactNameConfidence,
            Status = firm.Status,
            Notes = firm.Notes,
            LastContactedAt = firm.LastContactedAt,
            ContactCount = firm.ContactCount,
            CreatedAt = firm.CreatedAt,
            HasUsableContactName = firm.HasUsableContactName
        };
    }
}
