using Auth.Models.Constants;
using Auth.Models.Data;
using Auth.Models.DTOs.Suggestions;
using Auth.Models.Entities.Suggestions;
using Auth.Models.Enums.Suggestions;
using Auth.Models.Exceptions;
using Auth.Services.Interfaces.Notifications;
using Auth.Services.Interfaces.Suggestions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services.Suggestions
{
    /// <inheritdoc cref="ISuggestionService"/>
    public class SuggestionService : ISuggestionService
    {
        /// <summary>
        /// Per-person daily cap.
        ///
        /// Not really about abuse — it is a board of a few dozen people who know each other.
        /// It is about the board staying readable: three considered notes are worth more than
        /// twenty thoughts typed in one sitting, and the limit nudges people to write the
        /// former.
        /// </summary>
        public const int MaxPerUserPerDay = 3;

        private const int MaxBodyLength = 500;
        private const int MinBodyLength = 10;
        private const int PaletteSize = 6;

        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notifications;
        private readonly ILogger<SuggestionService> _logger;

        public SuggestionService(
            ApplicationDbContext context,
            INotificationService notifications,
            ILogger<SuggestionService> logger)
        {
            _context = context;
            _notifications = notifications;
            _logger = logger;
        }

        // ── Reading ───────────────────────────────────────────────────────────

        public async Task<SuggestionBoardDto> GetBoardAsync(
            string userId, bool canModerate, CancellationToken cancellationToken = default)
        {
            // Moderators see hidden notes so they can review what they hid; everyone else
            // sees the board as it stands.
            var query = _context.Suggestions.AsNoTracking();
            if (!canModerate) query = query.Where(s => !s.IsHidden);

            var suggestions = await query
                .OrderByDescending(s => s.CreatedAt)
                .Take(200)
                .ToListAsync(cancellationToken);

            var ids = suggestions.Select(s => s.Id).ToList();

            // One query for the caller's votes rather than a per-note lookup — the board
            // renders every note at once, so an N+1 here is an N+1 on the hot path.
            var myVotes = await _context.SuggestionVotes
                .AsNoTracking()
                .Where(v => v.UserId == userId && ids.Contains(v.SuggestionId))
                .Select(v => v.SuggestionId)
                .ToListAsync(cancellationToken);

            var votedSet = myVotes.ToHashSet();

            return new SuggestionBoardDto
            {
                Items = suggestions.Select(s => ToDto(s, userId, votedSet.Contains(s.Id))).ToList(),
                CanModerate = canModerate,
                RemainingToday = await RemainingTodayAsync(userId, cancellationToken)
            };
        }

        private async Task<int> RemainingTodayAsync(string userId, CancellationToken cancellationToken)
        {
            var since = DateTime.UtcNow.Date;

            var used = await _context.Suggestions
                .CountAsync(s => s.UserId == userId && s.CreatedAt >= since, cancellationToken);

            return Math.Max(0, MaxPerUserPerDay - used);
        }

        // ── Writing ───────────────────────────────────────────────────────────

        public async Task<SuggestionDto> CreateAsync(
            string userId, string authorName, CreateSuggestionRequest request,
            CancellationToken cancellationToken = default)
        {
            var body = request.Body?.Trim() ?? string.Empty;

            if (body.Length < MinBodyLength)
            {
                throw new ValidationException(
                    $"A suggestion needs at least {MinBodyLength} characters — say a little about why.");
            }

            if (body.Length > MaxBodyLength) body = body[..MaxBodyLength];

            if (await RemainingTodayAsync(userId, cancellationToken) <= 0)
            {
                throw new ValidationException(
                    $"You've posted {MaxPerUserPerDay} suggestions today. Have a think and come back tomorrow.");
            }

            var suggestion = new Suggestion
            {
                UserId = userId,
                AuthorName = string.IsNullOrWhiteSpace(authorName) ? "A scholar" : authorName,
                IsAnonymous = request.IsAnonymous,
                Body = body,

                // Clamped rather than rejected: a bad colour index is a client bug, and
                // failing the whole post over cosmetics would lose what they wrote.
                ColorIndex = ((request.ColorIndex % PaletteSize) + PaletteSize) % PaletteSize,
                CreatedAt = DateTime.UtcNow,
                Status = SuggestionStatus.New
            };

            _context.Suggestions.Add(suggestion);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Suggestion {Id} posted by {User}.", suggestion.Id, userId);

            return ToDto(suggestion, userId, hasVoted: false);
        }

        public async Task<bool> DeleteAsync(
            string userId, int id, bool canModerate, CancellationToken cancellationToken = default)
        {
            var suggestion = await _context.Suggestions
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (suggestion is null) return false;

            // An author may withdraw their own note outright. Staff hide rather than delete,
            // so a moderation decision leaves a record — see SetHiddenAsync.
            if (suggestion.UserId != userId && !canModerate)
            {
                throw new ForbiddenAccessException("You can only remove your own suggestion.");
            }

            _context.Suggestions.Remove(suggestion);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        // ── Voting ────────────────────────────────────────────────────────────

        public async Task<SuggestionDto> ToggleVoteAsync(
            string userId, int id, CancellationToken cancellationToken = default)
        {
            var suggestion = await _context.Suggestions
                .FirstOrDefaultAsync(s => s.Id == id && !s.IsHidden, cancellationToken)
                ?? throw new NotFoundException("Suggestion", id.ToString());

            var existing = await _context.SuggestionVotes
                .FirstOrDefaultAsync(v => v.SuggestionId == id && v.UserId == userId, cancellationToken);

            bool hasVoted;

            if (existing is not null)
            {
                _context.SuggestionVotes.Remove(existing);

                // Floored at zero. The denormalised count and the vote rows could in principle
                // drift — a negative badge on screen is a far worse failure than an
                // off-by-one, so it cannot go below zero whatever happens.
                suggestion.VoteCount = Math.Max(0, suggestion.VoteCount - 1);
                hasVoted = false;
            }
            else
            {
                _context.SuggestionVotes.Add(new SuggestionVote
                {
                    SuggestionId = id,
                    UserId = userId
                });

                suggestion.VoteCount += 1;
                hasVoted = true;
            }

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Double-click, or the same account voting from two tabs. The unique index
                // makes the loser fail; the user's intent is already satisfied by the winner.
                _context.ChangeTracker.Clear();
                var refreshed = await _context.Suggestions
                    .AsNoTracking()
                    .FirstAsync(s => s.Id == id, cancellationToken);

                return ToDto(refreshed, userId, hasVoted: true);
            }

            return ToDto(suggestion, userId, hasVoted);
        }

        // ── Moderation ────────────────────────────────────────────────────────

        public async Task<SuggestionDto> SetStatusAsync(
            int id, UpdateSuggestionStatusRequest request, string staffName,
            CancellationToken cancellationToken = default)
        {
            var suggestion = await _context.Suggestions
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new NotFoundException("Suggestion", id.ToString());

            var previous = suggestion.Status;

            suggestion.Status = request.Status;
            suggestion.StaffNote = string.IsNullOrWhiteSpace(request.StaffNote)
                ? null
                : request.StaffNote.Trim();
            suggestion.StatusChangedAt = DateTime.UtcNow;
            suggestion.StatusChangedByName = staffName;

            await _context.SaveChangesAsync(cancellationToken);

            // Tell the author. This is the whole reason the board has statuses — somebody who
            // wrote a suggestion three weeks ago is not going to keep checking a page to see
            // whether anything happened to it.
            if (previous != request.Status)
            {
                await _notifications.CreateAsync(new CreateNotificationRequest
                {
                    UserId = suggestion.UserId,
                    MessageKey = NotificationKeys.SuggestionStatusChanged,
                    Params = new Dictionary<string, string>
                    {
                        ["status"] = request.Status.ToString(),
                        ["excerpt"] = Excerpt(suggestion.Body)
                    },
                    DedupeKey = $"suggestion-status:{id}:{request.Status}",
                    WantsEmail = true,
                    WantsPush = true
                }, cancellationToken);
            }

            _logger.LogInformation(
                "Suggestion {Id} moved from {From} to {To} by {Staff}.",
                id, previous, request.Status, staffName);

            return ToDto(suggestion, suggestion.UserId, hasVoted: false);
        }

        public async Task<bool> SetHiddenAsync(
            int id, bool hidden, CancellationToken cancellationToken = default)
        {
            var affected = await _context.Suggestions
                .Where(s => s.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsHidden, hidden), cancellationToken);

            return affected > 0;
        }

        // ── Mapping ───────────────────────────────────────────────────────────

        private static string Excerpt(string body) =>
            body.Length <= 60 ? body : body[..60].TrimEnd() + "…";

        private static SuggestionDto ToDto(Suggestion s, string callerId, bool hasVoted) => new()
        {
            Id = s.Id,

            // The name is withheld from the payload entirely when anonymous, rather than sent
            // and hidden by CSS. Anything that reaches the browser is readable by whoever is
            // sitting at it.
            AuthorName = s.IsAnonymous ? null : s.AuthorName,

            IsMine = s.UserId == callerId,
            Body = s.Body,
            ColorIndex = s.ColorIndex,
            CreatedAt = s.CreatedAt,
            Status = s.Status,
            StaffNote = s.StaffNote,
            StatusChangedAt = s.StatusChangedAt,
            StatusChangedByName = s.StatusChangedByName,
            VoteCount = s.VoteCount,
            HasVoted = hasVoted
        };
    }
}
