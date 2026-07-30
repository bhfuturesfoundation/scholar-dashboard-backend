using Auth.Models.Constants;
using Auth.Models.Data;
using Auth.Models.DTOs;
using Auth.Models.Entities;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    public class ManagerService : IManagerService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<ManagerService> _logger;

        public ManagerService(UserManager<User> userManager, ILogger<ManagerService> logger, ApplicationDbContext context)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }
        public async Task<List<JournalAnswerResponse>> GetJournalForUserAsync(string scholarId, string monthYear)
        {
            var questions = await _context.Questions
            .Where(q => q.Active)
                .OrderBy(q => q.Order)
                .ToListAsync();

            var answers = await _context.Answers
                .Where(a => a.ScholarId == scholarId && a.MonthYear == monthYear)
                .ToListAsync();

            var result = questions.Select(q =>
            {
                var answer = answers.FirstOrDefault(a => a.QuestionId == q.QuestionId);

                return new JournalAnswerResponse
                {
                    QuestionId = q.QuestionId,
                    Text = q.Text,
                    Type = q.Type,
                    Order = q.Order,
                    Response = answer?.Response ?? string.Empty,
                    MonthYear = monthYear
                };
            }).ToList();

            return result;
        }
        public async Task<List<JournalSubmissionStatusDto>> GetUserSubmissionsAsync(string userId)
        {
            try
            {
                var submissions = await _context.JournalSubmissions
                    .Where(js => js.ScholarId == userId)
                    .OrderByDescending(js => js.MonthYear)
                    .Select(js => new JournalSubmissionStatusDto
                    {
                        MonthYear = js.MonthYear,
                        Submitted = js.Submitted
                    })
                    .ToListAsync();

                return submissions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching journal submissions for user {UserId}", userId);
                throw new ApplicationException("An unexpected error occurred while fetching journal submissions.", ex);
            }
        }

        /// <summary>
        /// The overall-satisfaction question. Resolved by <c>Order</c> rather than a
        /// hard-coded QuestionId: the seeder assigns ids by insertion, so id 16 is only
        /// question 16 on a database that was seeded from empty. On any environment where
        /// questions were edited or re-seeded, the old <c>QuestionId == 16</c> filter
        /// silently scored the wrong question — or nothing at all.
        /// </summary>
        private const int SatisfactionQuestionOrder = 16;

        /// <summary>
        /// Roles that identify an FLS-portal account rather than a scholarship participant.
        /// Accounts holding only these are excluded from the scholar journal overview —
        /// they have no journal and were padding the list and the totals.
        /// </summary>
        private static readonly string[] NonScholarRoles =
        {
            AppRoles.FLSSpeaker, AppRoles.FLSAdmin, AppRoles.PartnerMember
        };

        public async Task<PagedResult<ScholarJournalOverviewDto>> GetJournalOverviewAsync(int page = 1, int pageSize = 100)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);

            try
            {
                var rolesDict = await _context.Roles
                    .AsNoTracking()
                    .ToDictionaryAsync(r => r.Id, r => r.Name);

                var nonScholarRoleIds = rolesDict
                    .Where(kvp => kvp.Value is not null && NonScholarRoles.Contains(kvp.Value))
                    .Select(kvp => kvp.Key)
                    .ToList();

                // Exclude accounts whose ONLY roles are FLS-portal roles. Filtering in the
                // query keeps the count and the page in agreement — previously TotalCount
                // counted every user while the page showed a filtered subset, so the
                // pager reported more scholars than it could ever display.
                var scholarQuery = _context.Users
                    .AsNoTracking()
                    .Where(u => !_context.UserRoles
                        .Where(ur => ur.UserId == u.Id)
                        .All(ur => nonScholarRoleIds.Contains(ur.RoleId)) ||
                        !_context.UserRoles.Any(ur => ur.UserId == u.Id));

                var totalCount = await scholarQuery.CountAsync();

                var users = await scholarQuery
                    .OrderBy(u => u.LastName)
                    .ThenBy(u => u.FirstName)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var userIds = users.Select(u => u.Id).ToList();

                var satisfactionQuestionId = await _context.Questions
                    .Where(q => q.Order == SatisfactionQuestionOrder)
                    .Select(q => (int?)q.QuestionId)
                    .FirstOrDefaultAsync();

                var userRoles = await _context.UserRoles
                    .Where(ur => userIds.Contains(ur.UserId))
                    .ToListAsync();

                var answers = satisfactionQuestionId is null
                    ? new List<Answer>()
                    : await _context.Answers
                        .AsNoTracking()
                        .Where(a => userIds.Contains(a.ScholarId) && a.QuestionId == satisfactionQuestionId)
                        .ToListAsync();

                // The authoritative record of what was actually submitted. The overview used
                // to infer this from the presence of a satisfaction answer, which disagreed
                // with the per-scholar detail page and produced ticks for months a scholar
                // had never submitted (and crosses for months they had, but left Q16 blank).
                var submissionFlags = await _context.JournalSubmissions
                    .AsNoTracking()
                    .Where(js => userIds.Contains(js.ScholarId))
                    .ToListAsync();

                if (satisfactionQuestionId is null)
                {
                    _logger.LogWarning(
                        "No question with Order {Order} exists — satisfaction scores will be unavailable.",
                        SatisfactionQuestionOrder);
                }

                var answersByUser = answers
                    .GroupBy(a => a.ScholarId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var submissionsByUser = submissionFlags
                    .GroupBy(js => js.ScholarId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var rolesByUser = userRoles
                    .GroupBy(ur => ur.UserId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(ur => rolesDict.TryGetValue(ur.RoleId, out var name) ? name : null)
                              .Where(n => !string.IsNullOrEmpty(n))
                              .Select(n => n!)
                              .ToList());

                var scholars = users.Select(u => new ScholarJournalOverviewDto
                {
                    Id = u.Id,
                    FirstName = u.FirstName ?? string.Empty,
                    LastName = u.LastName ?? string.Empty,
                    Email = u.Email ?? string.Empty,
                    Title = u.Title ?? string.Empty,
                    Roles = rolesByUser.TryGetValue(u.Id, out var roles) ? roles : new List<string>(),
                    Submissions = BuildSubmissions(
                        answersByUser.GetValueOrDefault(u.Id),
                        submissionsByUser.GetValueOrDefault(u.Id))
                }).ToList();

                return new PagedResult<ScholarJournalOverviewDto>
                {
                    Items = scholars,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching journal overview");
                throw new ApplicationException("An unexpected error occurred while fetching journal overview.", ex);
            }
        }

        /// <summary>
        /// Merges the two independent sources of monthly data — the submission flag and the
        /// satisfaction answer — into one row per month. A month appears if EITHER source
        /// has it, so a submitted month with no satisfaction answer is no longer invisible.
        /// </summary>
        internal static List<JournalSubmissionDto> BuildSubmissions(
            List<Answer>? satisfactionAnswers,
            List<JournalSubmission>? submissions)
        {
            var answersByMonth = (satisfactionAnswers ?? new List<Answer>())
                .GroupBy(a => a.MonthYear)
                .ToDictionary(g => g.Key, CalculateSatisfactionScore);

            var submittedMonths = (submissions ?? new List<JournalSubmission>())
                .GroupBy(js => js.MonthYear)
                // A scholar can have more than one row per month in older data; treat the
                // month as submitted if any row says so.
                .ToDictionary(g => g.Key, g => g.Any(js => js.Submitted));

            return answersByMonth.Keys
                .Union(submittedMonths.Keys)
                .OrderByDescending(m => m, StringComparer.Ordinal)
                .Select(month => new JournalSubmissionDto
                {
                    MonthYear = month,
                    Submitted = submittedMonths.GetValueOrDefault(month),
                    SatisfactionScore = answersByMonth.TryGetValue(month, out var score) ? score : null
                })
                .ToList();
        }

        /// <summary>Scales the 1-10 satisfaction answer to a 0-100 percentage. Null when unparseable.</summary>
        private static int? CalculateSatisfactionScore(IEnumerable<Answer> answersForMonth)
        {
            double total = 0;
            var count = 0;

            foreach (var ans in answersForMonth)
            {
                if (int.TryParse(ans.Response, out var val))
                {
                    total += val;
                    count++;
                }
            }

            return count > 0 ? (int)Math.Round(total / count * 10) : null;
        }
        public async Task<UserDetailsResponse?> GetUserByIdAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return null;

            var roles = await _userManager.GetRolesAsync(user);

            return new UserDetailsResponse
            {
                Id = user.Id,
                FirstName = user.FirstName ?? string.Empty,
                Title = user.Title ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                Email = user.Email ?? string.Empty,
                Roles = roles.ToList(),
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            };
        }
    }
}
