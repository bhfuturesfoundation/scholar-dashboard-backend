using Auth.Models.Data;
using Auth.Models.DTOs.Engagement;
using Auth.Models.Entities.Engagement;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces.Engagement;
using Auth.Services.Interfaces.Notifications;
using Auth.Models.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Engagement
{
    /// <summary>
    /// Scholar-to-scholar recognition.
    ///
    /// The rules exist to keep this from turning into a scoreboard between people who see
    /// each other every week: positive categories only, no self-kudos, a daily cap per
    /// recipient so it cannot be farmed, and moderation by hiding rather than deleting.
    /// </summary>
    public class KudosService : IKudosService
    {
        /// <summary>
        /// How many times one scholar may recognise the same person per day.
        ///
        /// Without a cap, "most kudos" becomes a measure of who has the most enthusiastic
        /// friend rather than who helped the most people — and the badges built on it become
        /// meaningless.
        /// </summary>
        private const int MaxPerRecipientPerDay = 1;

        private const int MaxMessageLength = 300;

        private readonly ApplicationDbContext _context;
        private readonly IScholarProgressService _progress;
        private readonly INotificationService _notifications;
        private readonly ILogger<KudosService> _logger;

        public KudosService(
            ApplicationDbContext context,
            IScholarProgressService progress,
            INotificationService notifications,
            ILogger<KudosService> logger)
        {
            _context = context;
            _progress = progress;
            _notifications = notifications;
            _logger = logger;
        }

        public List<KudosCategoryDto> GetCategories() =>
            KudosCategories.All
                .Select(kvp => new KudosCategoryDto { Key = kvp.Key, Label = kvp.Value })
                .ToList();

        public async Task<KudosDto> GiveAsync(
            string fromUserId, string toUserId, string category, string? message,
            CancellationToken cancellationToken = default)
        {
            if (fromUserId == toUserId)
                throw new ValidationException("You can't give kudos to yourself.");

            if (!KudosCategories.IsValid(category))
                throw new ValidationException("Choose one of the available categories.");

            var recipient = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == toUserId, cancellationToken)
                ?? throw new NotFoundException("Scholar", toUserId);

            if (!recipient.IsActive)
                throw new ValidationException("That account is no longer active.");

            var since = DateTime.UtcNow.Date;
            var todayCount = await _context.Kudos
                .CountAsync(k => k.FromUserId == fromUserId
                                 && k.ToUserId == toUserId
                                 && k.CreatedAt >= since, cancellationToken);

            if (todayCount >= MaxPerRecipientPerDay)
            {
                throw new ValidationException(
                    "You've already recognised this person today. Kudos mean more when they're not repeated.");
            }

            var trimmed = string.IsNullOrWhiteSpace(message)
                ? null
                : message.Trim()[..Math.Min(message.Trim().Length, MaxMessageLength)];

            var kudos = new Kudos
            {
                FromUserId = fromUserId,
                ToUserId = toUserId,
                Category = category,
                Message = trimmed,
                CreatedAt = DateTime.UtcNow
            };

            _context.Kudos.Add(kudos);
            await _context.SaveChangesAsync(cancellationToken);

            // Both sides can earn a badge from this — the giver for generosity, the recipient
            // for being recognised.
            await _progress.EvaluateAsync(fromUserId, cancellationToken);
            await _progress.EvaluateAsync(toUserId, cancellationToken);

            // Tell the recipient. Collapsed under a shared key so a well-liked scholar who
            // gets recognised five times in an afternoon sees one line rather than five —
            // recognition that arrives as a burst of identical rows reads as noise.
            var giver = await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == fromUserId, cancellationToken);

            await _notifications.CreateAsync(new CreateNotificationRequest
            {
                UserId = toUserId,
                MessageKey = NotificationKeys.KudosReceived,
                Params = new Dictionary<string, string>
                {
                    ["fromName"] = $"{giver?.FirstName} {giver?.LastName}".Trim() is { Length: > 0 } name
                        ? name
                        : "A scholar",
                    ["categoryLabel"] = KudosCategories.All.GetValueOrDefault(category, category)
                },
                CollapseKey = "kudos",
                WantsEmail = true,
                WantsPush = true
            }, cancellationToken);

            _logger.LogInformation("Kudos given from {From} to {To} ({Category}).", fromUserId, toUserId, category);

            return await MapAsync(kudos.Id, cancellationToken);
        }

        public async Task<KudosSummaryDto> GetForUserAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            var received = await Query()
                .Where(k => k.ToUserId == userId && !k.IsHidden)
                .OrderByDescending(k => k.CreatedAt)
                .Take(50)
                .ToListAsync(cancellationToken);

            var given = await Query()
                .Where(k => k.FromUserId == userId && !k.IsHidden)
                .OrderByDescending(k => k.CreatedAt)
                .Take(50)
                .ToListAsync(cancellationToken);

            return new KudosSummaryDto
            {
                ReceivedCount = received.Count,
                GivenCount = given.Count,
                Received = received.Select(Map).ToList(),
                Given = given.Select(Map).ToList(),
                ReceivedByCategory = received
                    .GroupBy(k => k.Category)
                    .ToDictionary(
                        g => KudosCategories.All.TryGetValue(g.Key, out var label) ? label : g.Key,
                        g => g.Count())
            };
        }

        public async Task<List<KudosDto>> GetRecentAsync(
            int limit = 20, CancellationToken cancellationToken = default)
        {
            // A shared feed of recent recognition. Messages are shown as written, so this is
            // the surface moderation exists for.
            var recent = await Query()
                .Where(k => !k.IsHidden)
                .OrderByDescending(k => k.CreatedAt)
                .Take(Math.Clamp(limit, 1, 100))
                .ToListAsync(cancellationToken);

            return recent.Select(Map).ToList();
        }

        public async Task HideAsync(int kudosId, CancellationToken cancellationToken = default)
        {
            var kudos = await _context.Kudos.FirstOrDefaultAsync(k => k.Id == kudosId, cancellationToken)
                ?? throw new NotFoundException("Kudos", kudosId.ToString());

            // Hidden, not deleted: the sender isn't left confused about where it went, and the
            // moderation decision remains inspectable.
            kudos.IsHidden = true;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Kudos {Id} hidden by staff.", kudosId);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private IQueryable<Kudos> Query() =>
            _context.Kudos
                .AsNoTracking()
                .Include(k => k.FromUser)
                .Include(k => k.ToUser);

        private async Task<KudosDto> MapAsync(int id, CancellationToken cancellationToken)
        {
            var kudos = await Query().FirstAsync(k => k.Id == id, cancellationToken);
            return Map(kudos);
        }

        private static KudosDto Map(Kudos k) => new()
        {
            Id = k.Id,
            FromUserId = k.FromUserId,
            FromName = $"{k.FromUser?.FirstName} {k.FromUser?.LastName}".Trim(),
            ToUserId = k.ToUserId,
            ToName = $"{k.ToUser?.FirstName} {k.ToUser?.LastName}".Trim(),
            Category = k.Category,
            CategoryLabel = KudosCategories.All.TryGetValue(k.Category, out var label) ? label : k.Category,
            Message = k.Message,
            CreatedAt = k.CreatedAt
        };
    }
}
