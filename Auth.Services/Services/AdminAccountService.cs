using System.ComponentModel.DataAnnotations;
using Auth.Models.Constants;
using Auth.Models.Data;
using Auth.Models.DTOs.Admin;
using Auth.Models.Entities;
using Auth.Models.Exceptions;
using Auth.Models.Request;
using Auth.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = Auth.Models.Exceptions.ValidationException;

namespace Auth.Services.Services
{
    /// <inheritdoc cref="IAdminAccountService"/>
    public class AdminAccountService : IAdminAccountService
    {
        private const int MaxNameLength = 100;

        private static readonly EmailAddressAttribute EmailFormat = new();

        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;
        private readonly IAccountService _accountService;
        private readonly IPasswordResetService _passwordReset;
        private readonly IAuditService _auditService;
        private readonly ILogger<AdminAccountService> _logger;

        public AdminAccountService(
            ApplicationDbContext context,
            UserManager<User> userManager,
            IAccountService accountService,
            IPasswordResetService passwordReset,
            IAuditService auditService,
            ILogger<AdminAccountService> logger)
        {
            _context = context;
            _userManager = userManager;
            _accountService = accountService;
            _passwordReset = passwordReset;
            _auditService = auditService;
            _logger = logger;
        }

        public async Task<AdminAccountDetailDto> GetAccountAsync(
            string userId, CancellationToken cancellationToken = default)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Generation)
                .Include(u => u.Mentor)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException("User", userId);

            var roles = await _userManager.GetRolesAsync(user);
            var now = DateTime.UtcNow;

            var sessions = _context.RefreshTokens.Where(t => t.UserId == userId);

            var activeSessions = await sessions
                .CountAsync(t => t.RevokedAt == null && t.ExpiryTime > now, cancellationToken);

            var lastSignInAt = await sessions
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => (DateTime?)t.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var isScholar = roles.Contains(AppRoles.User);

            return new AdminAccountDetailDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName ?? string.Empty,
                LastName = user.LastName ?? string.Empty,
                Title = user.Title ?? string.Empty,
                Roles = roles.ToList(),
                IsActive = user.IsActive,
                EmailConfirmed = user.EmailConfirmed,
                CreatedAt = user.CreatedAt,
                LockedOutUntil = user.LockoutEnd > DateTimeOffset.UtcNow ? user.LockoutEnd : null,
                FailedSignInAttempts = user.AccessFailedCount,
                MustChangePassword = user.MustChangePassword,
                ActiveSessions = activeSessions,
                LastSignInAt = lastSignInAt,
                ScholarStatus = isScholar ? (int)user.ScholarStatus : null,
                GenerationName = user.Generation?.Name,
                MentorName = user.Mentor is null
                    ? null
                    : $"{user.Mentor.FirstName} {user.Mentor.LastName}".Trim()
            };
        }

        public async Task<AdminAccountDetailDto> UpdateAccountAsync(
            string adminId, string userId, AdminUpdateUserRequest request, CancellationToken cancellationToken = default)
        {
            var user = await FindAsync(userId);

            var firstName = Clean(request.FirstName);
            var lastName = Clean(request.LastName);
            var email = request.Email?.Trim();

            if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            {
                throw new ValidationException("Both a first and last name are required.");
            }

            if (string.IsNullOrWhiteSpace(email) || !EmailFormat.IsValid(email))
            {
                throw new ValidationException("Enter a valid email address.");
            }

            var changes = new List<string>();

            if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            {
                var owner = await _userManager.FindByEmailAsync(email);
                if (owner is not null && owner.Id != user.Id)
                {
                    throw new ConflictException("Another account already uses this email address.");
                }

                changes.Add($"Email '{user.Email}' -> '{email}'");

                // UserName is the sign-in identifier and has always mirrored the email. Both
                // go through UserManager so the normalized columns and security stamp move too.
                EnsureSucceeded(await _userManager.SetEmailAsync(user, email));
                EnsureSucceeded(await _userManager.SetUserNameAsync(user, email));
            }

            if (user.FirstName != firstName || user.LastName != lastName)
            {
                changes.Add($"Name '{user.FirstName} {user.LastName}' -> '{firstName} {lastName}'");
                user.FirstName = firstName;
                user.LastName = lastName;
            }

            if (changes.Count > 0)
            {
                user.UpdatedAt = DateTime.UtcNow;
                EnsureSucceeded(await _userManager.UpdateAsync(user));

                await _auditService.LogAsync(
                    "Admin.AccountUpdated", adminId, $"Target={userId} {string.Join("; ", changes)}");
            }

            return await GetAccountAsync(userId, cancellationToken);
        }

        public async Task UnlockAsync(string adminId, string userId, CancellationToken cancellationToken = default)
        {
            var user = await FindAsync(userId);

            EnsureSucceeded(await _userManager.SetLockoutEndDateAsync(user, null));
            EnsureSucceeded(await _userManager.ResetAccessFailedCountAsync(user));

            await _auditService.LogAsync("Admin.AccountUnlocked", adminId, $"Target={userId}");
        }

        public async Task SendPasswordResetEmailAsync(
            string adminId, string userId, CancellationToken cancellationToken = default)
        {
            var user = await FindAsync(userId);

            await _passwordReset.SendResetLinkAsync(user);

            await _auditService.LogAsync("Admin.PasswordResetEmailSent", adminId, $"Target={userId}");
        }

        public async Task<TemporaryPasswordDto> SetTemporaryPasswordAsync(
            string adminId, string userId, string? ipAddress = null, CancellationToken cancellationToken = default)
        {
            if (adminId == userId)
            {
                // An admin who knows their own password should change it normally; one who
                // doesn't needs another admin, not a button that hands them a fresh one.
                throw new ValidationException("Use Change password for your own account.");
            }

            var user = await FindAsync(userId);
            var password = TemporaryPassword.Generate();

            // Through a reset token rather than remove-then-add: one write, so a failure
            // leaves the old password in place instead of an account with none.
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            EnsureSucceeded(await _userManager.ResetPasswordAsync(user, token, password));

            user.MustChangePassword = true;
            user.UpdatedAt = DateTime.UtcNow;
            EnsureSucceeded(await _userManager.UpdateAsync(user));

            EnsureSucceeded(await _userManager.SetLockoutEndDateAsync(user, null));
            EnsureSucceeded(await _userManager.ResetAccessFailedCountAsync(user));

            // Whoever held the old password should not stay signed in with it.
            await _accountService.RevokeSessionsAsync(
                userId, "Temporary password set by an administrator", ipAddress, cancellationToken);

            await _auditService.LogAsync("Admin.TemporaryPasswordSet", adminId, $"Target={userId}", ipAddress);

            _logger.LogInformation("Admin {AdminId} set a temporary password for {UserId}.", adminId, userId);

            return new TemporaryPasswordDto { TemporaryPassword = password };
        }

        public async Task<int> SignOutEverywhereAsync(
            string adminId, string userId, string? ipAddress = null, CancellationToken cancellationToken = default)
        {
            await FindAsync(userId);

            var live = await _accountService.RevokeSessionsAsync(
                userId, "Signed out of all devices by an administrator", ipAddress, cancellationToken);

            await _auditService.LogAsync(
                "Admin.SignedOutEverywhere", adminId, $"Target={userId} Revoked={live}", ipAddress);

            return live;
        }

        private async Task<User> FindAsync(string userId) =>
            await _userManager.FindByIdAsync(userId) ?? throw new NotFoundException("User", userId);

        private static void EnsureSucceeded(IdentityResult result)
        {
            if (!result.Succeeded)
            {
                throw new ValidationException(string.Join(" ", result.Errors.Select(e => e.Description)));
            }
        }

        private static string? Clean(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            return collapsed.Length <= MaxNameLength ? collapsed : collapsed[..MaxNameLength];
        }
    }
}
