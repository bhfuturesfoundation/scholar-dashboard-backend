namespace Auth.Models.Entities
{
    public class AuditEvent
    {
        public int Id { get; set; }

        /// <summary>Null for anonymous events like failed login attempts.</summary>
        public string? UserId { get; set; }
        public User? User { get; set; }

        /// <summary>e.g. "Login.Success", "Login.Failed", "Role.Updated", "Password.Reset"</summary>
        public string EventType { get; set; } = string.Empty;

        /// <summary>Optional JSON or plain-text detail (never store raw passwords).</summary>
        public string? Payload { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string? IpAddress { get; set; }
    }
}
