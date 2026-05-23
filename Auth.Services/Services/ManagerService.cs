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

        public async Task<PagedResult<ScholarJournalOverviewDto>> GetJournalOverviewAsync(int page = 1, int pageSize = 100)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);

            try
            {
                var totalCount = await _context.Users.CountAsync();

                // Only load a single page of users — avoids pulling every user on every request
                var users = await _context.Users
                    .OrderBy(u => u.LastName)
                    .ThenBy(u => u.FirstName)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var userIds = users.Select(u => u.Id).ToList();

                // Scope all supporting queries to just this page's users
                var rolesDict = await _context.Roles.ToDictionaryAsync(r => r.Id, r => r.Name);
                var userRoles = await _context.UserRoles
                    .Where(ur => userIds.Contains(ur.UserId))
                    .ToListAsync();
                var answers = await _context.Answers
                    .Where(a => userIds.Contains(a.ScholarId) && a.QuestionId == 16)
                    .ToListAsync();

                static int CalculateSatisfactionScore(IEnumerable<Answer> answersForMonth)
                {
                    double total = 0;
                    int count = 0;
                    foreach (var ans in answersForMonth)
                    {
                        if (int.TryParse(ans.Response, out int val))
                        {
                            total += val;
                            count++;
                        }
                    }
                    // Scale 1-10 → 0-100 %
                    return count > 0 ? (int)Math.Round((total / count) * 10) : 0;
                }

                var scholars = users.Select(u =>
                {
                    var answersByMonth = answers
                        .Where(a => a.ScholarId == u.Id)
                        .GroupBy(a => a.MonthYear);

                    var submissions = answersByMonth.Select(g => new JournalSubmissionDto
                    {
                        MonthYear = g.Key,
                        SatisfactionScore = CalculateSatisfactionScore(g)
                    }).ToList();

                    return new ScholarJournalOverviewDto
                    {
                        Id = u.Id,
                        FirstName = u.FirstName ?? string.Empty,
                        LastName = u.LastName ?? string.Empty,
                        Email = u.Email ?? string.Empty,
                        Title = u.Title ?? string.Empty,
                        Roles = userRoles
                            .Where(ur => ur.UserId == u.Id)
                            .Select(ur => rolesDict.TryGetValue(ur.RoleId, out var roleName) ? roleName! : "")
                            .Where(rn => !string.IsNullOrEmpty(rn))
                            .ToList(),
                        Submissions = submissions
                    };
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
