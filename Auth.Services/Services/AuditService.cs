using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuditService> _logger;

        public AuditService(ApplicationDbContext context, ILogger<AuditService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task LogAsync(string eventType, string? userId = null, string? payload = null, string? ipAddress = null)
        {
            try
            {
                _context.AuditEvents.Add(new AuditEvent
                {
                    EventType = eventType,
                    UserId = userId,
                    Payload = payload,
                    IpAddress = ipAddress,
                    Timestamp = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Audit failures must never break the main request flow.
                _logger.LogError(ex, "Failed to persist audit event {EventType} for user {UserId}", eventType, userId);
            }
        }
    }
}
