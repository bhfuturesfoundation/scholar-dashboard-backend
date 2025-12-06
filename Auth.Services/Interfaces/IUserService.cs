using Auth.Models.Entities;
using Auth.Models.Request;
using Auth.Models.Response;
using Microsoft.AspNetCore.Identity;

namespace Auth.Services.Interfaces
{
    public interface IUserService
    {
        Task<string> GetUserTitleAsync(string userId);
        Task<CurrentUserResponse> GetCurrentUserAsync(string userId);
        Task<User> CreateUserAsync(RegisterRequest request);
        Task<(bool Succeeded, User User, bool RequiresTwoFactor, bool EmailNotConfirmed)> VerifyCredentialsAsync(string email, string password);
        Task<string> GenerateEmailConfirmationTokenAsync(string userId);
        Task<bool> ConfirmEmailAsync(string userId, string token);
        Task<User> GetUserByEmailAsync(string email);
        Task<string> GetUserEmailByIdAsync(string userId);
        Task ChangePasswordAsync(string userId, string currentPassword, string newPassword);
        Task<string> GeneratePasswordResetTokenAsync(User user);
        Task<IdentityResult> ResetPasswordAsync(User user, string token, string newPassword);
        Task<User?> GetByEmailAsync(string email);
        Task UpdateUserAsync(User user);
    }
}