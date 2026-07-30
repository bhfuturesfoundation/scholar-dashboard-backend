using Auth.Models.DTOs.Operations;

namespace Auth.Services.Interfaces.Operations
{
    /// <summary>
    /// Read-only operational view of the running deployment, for the admin console.
    /// Nothing here mutates state, and nothing here returns a secret value.
    /// </summary>
    public interface IOperationsService
    {
        /// <summary>
        /// Runs every subsystem check. Individual checks report failure rather than
        /// throwing — this is what someone opens when the app is already misbehaving.
        /// </summary>
        Task<DeployHealthDto> GetHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Which tracked environment variables are set. Presence only — values are never
        /// read into the response.
        /// </summary>
        EnvironmentStatusDto GetEnvironmentStatus();
    }
}
