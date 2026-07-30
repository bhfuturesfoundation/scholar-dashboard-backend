using Auth.Models.Data;
using Auth.Models.DTOs.Mailing;
using Auth.Models.Entities.Mailing;
using Auth.Models.Exceptions;
using Auth.Models.Request.Mailing;
using Auth.Services.Interfaces.Mailing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Mailing
{
    public class MailingTaxonomyService : IMailingTaxonomyService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<MailingTaxonomyService> _logger;

        public MailingTaxonomyService(ApplicationDbContext context, ILogger<MailingTaxonomyService> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ── Groups ────────────────────────────────────────────────────────────

        public async Task<List<FirmGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default)
        {
            // Counts computed in the projection so the list screen doesn't fire a query per row.
            return await _context.FirmGroups
                .AsNoTracking()
                .OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
                .Select(g => new FirmGroupDto
                {
                    Id = g.Id,
                    Name = g.Name,
                    Slug = g.Slug,
                    Description = g.Description,
                    ColorHex = g.ColorHex,
                    SortOrder = g.SortOrder,
                    IsSystem = g.IsSystem,
                    FirmTypeCount = g.FirmTypes.Count,
                    FirmCount = g.FirmTypes.SelectMany(t => t.Firms).Count()
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<FirmGroupDto> CreateGroupAsync(
            UpsertFirmGroupRequest request, CancellationToken cancellationToken = default)
        {
            var slug = await UniqueSlugAsync(
                TextNormalizer.Slugify(request.Name),
                s => _context.FirmGroups.AnyAsync(g => g.Slug == s, cancellationToken));

            var group = new FirmGroup
            {
                Name = request.Name.Trim(),
                Slug = slug,
                Description = request.Description,
                ColorHex = request.ColorHex,
                SortOrder = request.SortOrder
            };

            _context.FirmGroups.Add(group);
            await _context.SaveChangesAsync(cancellationToken);

            return (await GetGroupsAsync(cancellationToken)).First(g => g.Id == group.Id);
        }

        public async Task<FirmGroupDto> UpdateGroupAsync(
            int id, UpsertFirmGroupRequest request, CancellationToken cancellationToken = default)
        {
            var group = await _context.FirmGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
                ?? throw new NotFoundException("Firm group", id.ToString());

            group.Name = request.Name.Trim();
            group.Description = request.Description;
            group.ColorHex = request.ColorHex;
            group.SortOrder = request.SortOrder;
            group.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return (await GetGroupsAsync(cancellationToken)).First(g => g.Id == id);
        }

        public async Task DeleteGroupAsync(int id, CancellationToken cancellationToken = default)
        {
            var group = await _context.FirmGroups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken)
                ?? throw new NotFoundException("Firm group", id.ToString());

            // Seeded taxonomy can be renamed but not removed, so a campaign or template can
            // never end up pointing at a vanished category.
            if (group.IsSystem)
                throw new ValidationException("Built-in groups can be renamed but not deleted.");

            // Types survive, ungrouped — configured by SetNull on the FK.
            _context.FirmGroups.Remove(group);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // ── Types ─────────────────────────────────────────────────────────────

        public async Task<List<FirmTypeDto>> GetTypesAsync(CancellationToken cancellationToken = default)
        {
            return await _context.FirmTypes
                .AsNoTracking()
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
                .Select(t => new FirmTypeDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Slug = t.Slug,
                    FirmGroupId = t.FirmGroupId,
                    FirmGroupName = t.FirmGroup != null ? t.FirmGroup.Name : null,
                    Description = t.Description,
                    MatchKeywords = t.MatchKeywords,
                    ColorHex = t.ColorHex,
                    SortOrder = t.SortOrder,
                    IsSystem = t.IsSystem,
                    FirmCount = t.Firms.Count,
                    TemplateCount = t.Templates.Count
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<FirmTypeDto> CreateTypeAsync(
            UpsertFirmTypeRequest request, CancellationToken cancellationToken = default)
        {
            var slug = await UniqueSlugAsync(
                TextNormalizer.Slugify(request.Name),
                s => _context.FirmTypes.AnyAsync(t => t.Slug == s, cancellationToken));

            var type = new FirmType
            {
                Name = request.Name.Trim(),
                Slug = slug,
                FirmGroupId = request.FirmGroupId,
                Description = request.Description,
                MatchKeywords = NormalizeKeywords(request.MatchKeywords),
                ColorHex = request.ColorHex,
                SortOrder = request.SortOrder
            };

            _context.FirmTypes.Add(type);
            await _context.SaveChangesAsync(cancellationToken);

            return (await GetTypesAsync(cancellationToken)).First(t => t.Id == type.Id);
        }

        public async Task<FirmTypeDto> UpdateTypeAsync(
            int id, UpsertFirmTypeRequest request, CancellationToken cancellationToken = default)
        {
            var type = await _context.FirmTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                ?? throw new NotFoundException("Firm type", id.ToString());

            type.Name = request.Name.Trim();
            type.FirmGroupId = request.FirmGroupId;
            type.Description = request.Description;
            type.MatchKeywords = NormalizeKeywords(request.MatchKeywords);
            type.ColorHex = request.ColorHex;
            type.SortOrder = request.SortOrder;
            type.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            return (await GetTypesAsync(cancellationToken)).First(t => t.Id == id);
        }

        public async Task DeleteTypeAsync(int id, CancellationToken cancellationToken = default)
        {
            var type = await _context.FirmTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                ?? throw new NotFoundException("Firm type", id.ToString());

            if (type.IsSystem)
                throw new ValidationException("Built-in types can be renamed but not deleted.");

            // Firms and templates are detached rather than deleted (SetNull on both FKs).
            // Deleting a category must never delete the records filed under it.
            _context.FirmTypes.Remove(type);
            await _context.SaveChangesAsync(cancellationToken);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Appends -2, -3 … until the slug is free. Slugs are unique and used as the import
        /// column value, so a collision would silently file firms under the wrong type.
        /// </summary>
        private static async Task<string> UniqueSlugAsync(string baseSlug, Func<string, Task<bool>> exists)
        {
            if (string.IsNullOrWhiteSpace(baseSlug)) baseSlug = "item";

            var candidate = baseSlug;
            var suffix = 2;

            while (await exists(candidate))
            {
                candidate = $"{baseSlug}-{suffix}";
                suffix++;
            }

            return candidate;
        }

        private static string? NormalizeKeywords(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var keywords = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(k => k.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return keywords.Count == 0 ? null : string.Join(",", keywords);
        }
    }
}
