namespace Auth.Models.DTOs.Operations
{
    /// <summary>Traffic-light state for a single subsystem.</summary>
    public enum HealthState
    {
        /// <summary>Working.</summary>
        Healthy = 0,

        /// <summary>Working, but something needs attention before it becomes a problem.</summary>
        Degraded = 1,

        /// <summary>Not working.</summary>
        Unhealthy = 2,

        /// <summary>Deliberately not configured. Not a fault.</summary>
        NotConfigured = 3
    }

    public class HealthCheckDto
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public HealthState State { get; set; }

        /// <summary>One-line status, written for a human, e.g. "Connected in 12 ms".</summary>
        public string Summary { get; set; } = string.Empty;

        /// <summary>What to do about it, when the state isn't healthy.</summary>
        public string? Remediation { get; set; }

        /// <summary>Non-sensitive supporting values, e.g. latency or a version string.</summary>
        public Dictionary<string, string> Details { get; set; } = new();
    }

    public class DeployHealthDto
    {
        /// <summary>Worst state across all checks — what the header pill shows.</summary>
        public HealthState OverallState { get; set; }

        public string Environment { get; set; } = string.Empty;

        /// <summary>Time since the process started.</summary>
        public TimeSpan Uptime { get; set; }
        public DateTime StartedAtUtc { get; set; }

        public string? AppVersion { get; set; }

        /// <summary>Deployment identifier from the platform, when it exposes one.</summary>
        public string? DeploymentId { get; set; }

        public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;

        public List<HealthCheckDto> Checks { get; set; } = new();
    }

    public class EnvVarStatusDto
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        /// <summary>Required, Recommended or Optional.</summary>
        public string Importance { get; set; } = string.Empty;

        /// <summary>
        /// Whether a non-empty value is present. The value itself is never read or returned —
        /// this screen must be safe to open in front of anyone.
        /// </summary>
        public bool IsSet { get; set; }

        public string Purpose { get; set; } = string.Empty;

        /// <summary>What breaks while this is missing.</summary>
        public string? ConsequenceIfMissing { get; set; }
    }

    public class EnvironmentStatusDto
    {
        public int TotalTracked { get; set; }
        public int SetCount { get; set; }
        public int MissingRequiredCount { get; set; }
        public int MissingRecommendedCount { get; set; }

        public List<EnvVarStatusDto> Variables { get; set; } = new();

        /// <summary>
        /// Reminder shown above the table. The screen shows presence only, and saying so
        /// stops anyone asking for the values to be displayed "just for debugging".
        /// </summary>
        public string Notice { get; set; } =
            "Values are never read or displayed — only whether each variable is set.";
    }

    // ── Audit trail ───────────────────────────────────────────────────────────

    public class AuditEventDto
    {
        public int Id { get; set; }
        public string EventType { get; set; } = string.Empty;

        /// <summary>Free-text or JSON detail recorded with the event. Never contains secrets.</summary>
        public string? Payload { get; set; }

        public DateTime Timestamp { get; set; }
        public string? IpAddress { get; set; }

        /// <summary>Null for anonymous events such as a failed login for an unknown address.</summary>
        public string? UserId { get; set; }
        public string? UserDisplayName { get; set; }
        public string? UserEmail { get; set; }
    }

    public class AuditFilterOptionsDto
    {
        public List<string> Categories { get; set; } = new();

        /// <summary>Event types actually present, so a filter never returns an empty set.</summary>
        public List<string> EventTypes { get; set; } = new();
    }
}
