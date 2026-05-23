using Auth.Models.DTOs;
using Auth.Models.Response;

namespace Auth.Services.Interfaces
{
    public interface IAdminUserService
    {
        Task<PagedResult<UserWithRolesResponse>> GetAllUsersAsync(int page = 1, int pageSize = 50);
        Task<bool> UpdateUserRolesAsync(string userId, List<string> roles);
        Task<bool> UpdateUsersActiveStatusAsync(List<string> userIds, bool isActive);
    }
}
