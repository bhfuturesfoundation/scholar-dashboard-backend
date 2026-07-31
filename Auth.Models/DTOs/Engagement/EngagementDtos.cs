namespace Auth.Models.DTOs.Engagement
{
    public class ScholarTrendPointDto
    {
        public string MonthYear { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        /// <summary>Mean of the 1-10 skill ratings for that month. Null when none were numeric.</summary>
        public double? Score { get; set; }

        public bool Submitted { get; set; }
    }

    public class AchievementDto
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Tier { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;

        public bool IsEarned { get; set; }
        public DateTime? EarnedAt { get; set; }

        /// <summary>Earned but not yet acknowledged, so it can be celebrated once.</summary>
        public bool IsNew { get; set; }
    }

    public class ScholarProgressDto
    {
        public int TotalSubmissions { get; set; }
        public int CurrentStreak { get; set; }
        public int LongestStreak { get; set; }

        public string? GenerationName { get; set; }
        public string Status { get; set; } = string.Empty;

        public List<ScholarTrendPointDto> Trend { get; set; } = new();

        public double? LatestScore { get; set; }

        /// <summary>"up", "down" or "steady" over the last three data points.</summary>
        public string? TrendDirection { get; set; }
        public double? TrendChange { get; set; }

        /// <summary>
        /// Cohort median for the latest month. Null when the cohort is too small for a
        /// median to be anonymous — see MinimumCohortForComparison.
        /// </summary>
        public double? CohortMedian { get; set; }
        public int? CohortSize { get; set; }

        public List<AchievementDto> Achievements { get; set; } = new();
    }

    public class KudosDto
    {
        public int Id { get; set; }
        public string FromUserId { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public string ToUserId { get; set; } = string.Empty;
        public string ToName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string CategoryLabel { get; set; } = string.Empty;
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class KudosSummaryDto
    {
        public int ReceivedCount { get; set; }
        public int GivenCount { get; set; }

        public List<KudosDto> Received { get; set; } = new();
        public List<KudosDto> Given { get; set; } = new();

        /// <summary>Counts per category received, for a simple profile breakdown.</summary>
        public Dictionary<string, int> ReceivedByCategory { get; set; } = new();
    }

    public class KudosCategoryDto
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }
}
