using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Response;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

public class AdminUserService : IAdminUserService
{
    private readonly UserManager<User> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AdminUserService> _logger;

    public AdminUserService(UserManager<User> userManager, ILogger<AdminUserService> logger, ApplicationDbContext context)
    {
        _context = context;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<PagedResult<UserWithRolesResponse>> GetAllUsersAsync(int page = 1, int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _userManager.Users.OrderBy(u => u.CreatedAt);
        var totalCount = query.Count();

        // Paginate at the DB level so we only run GetRolesAsync for this page's users
        var users = query.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var result = new List<UserWithRolesResponse>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            result.Add(new UserWithRolesResponse
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Title = user.Title,
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                Roles = roles.ToList(),
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            });
        }

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
