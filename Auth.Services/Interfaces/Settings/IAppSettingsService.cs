using Auth.Models.DTOs.Settings;

namespace Auth.Services.Interfaces.Settings
{
    /// <summary>
    /// The programme settings an administrator may change from the app: journal deadline
    /// and reminders, the weekly digest, and the backup schedule. Everything else stays an
    /// environment variable.
    /// </summary>
    public interface IAppSettingsService
    {
        Task<List<AppSettingDto>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<List<AppSettingDto>> UpdateAsync(
            string adminId, UpdateAppSettingsRequest request, CancellationToken cancellationToken = default);

        /// <summary>Loads saved settings into configuration. Called once at startup.</summary>
        Task LoadAsync(CancellationToken cancellationToken = default);
    }
}
