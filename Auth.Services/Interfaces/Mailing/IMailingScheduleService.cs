using Auth.Models.DTOs.Mailing;
using Auth.Models.Request.Mailing;

namespace Auth.Services.Interfaces.Mailing
{
    /// <summary>
    /// Manages recurring sends. Execution happens in <c>MailingSchedulerService</c>, a hosted
    /// background service; this is only the configuration surface.
    /// </summary>
    public interface IMailingScheduleService
    {
        Task<List<MailingScheduleDto>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<MailingScheduleDto> CreateAsync(
            UpsertScheduleRequest request, string userId, string userName, CancellationToken cancellationToken = default);

        Task<MailingScheduleDto> UpdateAsync(
            int id, UpsertScheduleRequest request, CancellationToken cancellationToken = default);

        /// <summary>Pause or resume without losing the configuration.</summary>
        Task SetEnabledAsync(int id, bool enabled, CancellationToken cancellationToken = default);

        Task DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}
