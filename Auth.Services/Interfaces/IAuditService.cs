namespace Auth.Services.Interfaces
{
    public interface IAuditService
    {
        /// <summary>
        /// Persists an audit event. Never throws — failures are logged and swallowed
        /// so audit issues never break the main request flow.
        /// </summary>
        Task LogAsync(string eventType, string? userId = null, string? payload = null, string? ipAddress = null);
    }
}
