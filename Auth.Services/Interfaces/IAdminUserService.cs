using Auth.Models.DTOs;
using Auth.Models.Response;

namespace Auth.Services.Interfaces
{
    public interface IAdminUserService
    {
        Task<List<UserWithRolesResponse>> GetAllUsersAsync();
        Task<bool> UpdateUserRolesAsync(string userId, List<string> roles);
        Task<bool> UpdateUsersActiveStatusAsync(List<string> userIds, bool isActive);
    }
}
