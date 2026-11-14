using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public class AdminUserService : IAdminUserService
{
    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AdminUserService> _logger;
    private readonly IAccountService _accountService;

    public AdminUserService(
        UserManager<User> userManager,
        ILogger<AdminUserService> logger,
        ApplicationDbContext context,
        IAccountService accountService)
    {
        _accountService = accountService;
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<PagedResult<UserWithRolesResponse>> GetAllUsersAsync(int page = 1, int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _userManager.Users.AsNoTracking().OrderBy(u => u.CreatedAt);
        var totalCount = await query.CountAsync();
        var users = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        // One query for every role on the page rather than one GetRolesAsync per user.
        var userIds = users.Select(u => u.Id).ToList();
        var rolesByUser = (await _context.UserRoles
                .Where(ur => userIds.Contains(ur.UserId))
                .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
                .ToListAsync())
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).ToList());

        var result = users.Select(user => new UserWithRolesResponse
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            Title = user.Title,
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            Roles = rolesByUser.GetValueOrDefault(user.Id) ?? new List<string>(),
            IsActive = user.IsActive,
            IsLockedOut = user.LockoutEnd > DateTimeOffset.UtcNow,
            ScholarStatus = (int)user.ScholarStatus,
            CreatedAt = user.CreatedAt
        }).ToList();

        return new PagedResult<UserWithRolesResponse>
        {
            Items = result,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<bool> UpdateUsersActiveStatusAsync(List<string> userIds, bool isActive)
    {
        var users = _userManager.Users.Where(u => userIds.Contains(u.Id)).ToList();

        if (!users.Any()) return false;

        foreach (var user in users)
        {
            user.IsActive = isActive;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded) return false;

            // A deactivated account is signed out everywhere at once, not when its token
            // happens to expire.
            if (!isActive)
            {
                await _accountService.RevokeSessionsAsync(user.Id, "Account deactivated by an administrator");
            }
        }

        _logger.LogInformation("Active status updated for users: {UserIds} => {IsActive}", string.Join(", ", userIds), isActive);
        return true;
    }


    public async Task<bool> UpdateUserRolesAsync(string userId, List<string> roles)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return false;

        var currentRoles = await _userManager.GetRolesAsync(user);
        var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
        if (!removeResult.Succeeded) return false;

        var addResult = await _userManager.AddToRolesAsync(user, roles);
        if (!addResult.Succeeded) return false;

        _logger.LogInformation("Roles updated for user {UserId}: {Roles}", userId, string.Join(", ", roles));
        return true;
    }
}
