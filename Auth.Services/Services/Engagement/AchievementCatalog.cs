namespace Auth.Services.Services.Engagement
{
    public enum AchievementTier { Bronze = 1, Silver = 2, Gold = 3 }

    public record AchievementDefinition(
        string Key,
        string Name,
        string Description,
        string Category,
        AchievementTier Tier,
        string Icon);

    /// <summary>
    /// Every badge a scholar can earn.
    ///
    /// Held in code rather than the database because each badge is a rule, not a row —
    /// putting them in the database would mean either a rule engine or a schema change per
    /// badge. Keys are stable and must never be reused for a different meaning: a scholar
    /// who earned "journal-streak-6" keeps it forever, and repurposing the key would silently
    /// relabel their history.
    ///
    /// Thresholds are deliberately reachable. A badge nobody earns is decoration; the first
    /// journal entry and the first kudos given both award something, because the point is to
    /// reward starting, not only finishing.
    /// </summary>
    public static class AchievementCatalog
    {
        public const string JournalCategory = "Journal";
        public const string VolunteeringCategory = "Volunteering";
        public const string CommunityCategory = "Community";

        public static readonly IReadOnlyList<AchievementDefinition> All = new List<AchievementDefinition>
        {
            // ── Journal ───────────────────────────────────────────────────────
            new("journal-first", "First entry", "Submitted your first journal.", JournalCategory, AchievementTier.Bronze, "notebook-pen"),
            new("journal-streak-3", "Three in a row", "Submitted three months running.", JournalCategory, AchievementTier.Bronze, "flame"),
            new("journal-streak-6", "Half a year", "Six consecutive months.", JournalCategory, AchievementTier.Silver, "flame"),
            new("journal-streak-12", "A full year", "Twelve consecutive months without missing one.", JournalCategory, AchievementTier.Gold, "trophy"),
            new("journal-total-6", "Six entries", "Six journals submitted in total.", JournalCategory, AchievementTier.Bronze, "book"),
            new("journal-total-12", "Twelve entries", "Twelve journals submitted in total.", JournalCategory, AchievementTier.Silver, "book"),
            new("journal-total-24", "Twenty-four entries", "Two years' worth of reflection.", JournalCategory, AchievementTier.Gold, "book"),

            // ── Volunteering ──────────────────────────────────────────────────
            new("volunteer-10", "Ten hours", "Ten volunteering hours logged.", VolunteeringCategory, AchievementTier.Bronze, "heart-handshake"),
            new("volunteer-50", "Fifty hours", "Fifty volunteering hours logged.", VolunteeringCategory, AchievementTier.Silver, "heart-handshake"),
            new("volunteer-100", "One hundred hours", "A hundred hours given back.", VolunteeringCategory, AchievementTier.Gold, "award"),

            // ── Community ─────────────────────────────────────────────────────
            new("kudos-first-given", "Said thanks", "Gave your first kudos.", CommunityCategory, AchievementTier.Bronze, "hand-heart"),
            new("kudos-5-given", "Generous", "Recognised five different people.", CommunityCategory, AchievementTier.Silver, "hand-heart"),
            new("kudos-first-received", "Recognised", "Received your first kudos.", CommunityCategory, AchievementTier.Bronze, "sparkles"),
            new("kudos-10-received", "Well regarded", "Received ten kudos.", CommunityCategory, AchievementTier.Gold, "star"),
        };

        private static readonly Dictionary<string, AchievementDefinition> ByKey =
            All.ToDictionary(a => a.Key, StringComparer.Ordinal);

        public static AchievementDefinition? Find(string key) =>
            ByKey.TryGetValue(key, out var definition) ? definition : null;

        /// <summary>
        /// Streak thresholds paired with their badge, longest first so the evaluator awards
        /// every tier a scholar has passed rather than only the top one.
        /// </summary>
        public static readonly IReadOnlyList<(int Threshold, string Key)> JournalStreaks = new[]
        {
            (12, "journal-streak-12"), (6, "journal-streak-6"), (3, "journal-streak-3")
        };

        public static readonly IReadOnlyList<(int Threshold, string Key)> JournalTotals = new[]
        {
            (24, "journal-total-24"), (12, "journal-total-12"), (6, "journal-total-6")
        };

        public static readonly IReadOnlyList<(int Threshold, string Key)> VolunteerHours = new[]
        {
            (100, "volunteer-100"), (50, "volunteer-50"), (10, "volunteer-10")
        };
    }

    /// <summary>Fixed set of kudos categories, so recognition stays specific and positive.</summary>
    public static class KudosCategories
    {
        public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
        {
            ["helpful"] = "Helped me out",
            ["leadership"] = "Showed leadership",
            ["teamwork"] = "Great teammate",
            ["inspiring"] = "Inspired me",
            ["dedication"] = "Went above and beyond",
            ["welcoming"] = "Made me feel welcome",
        };

        public static bool IsValid(string? category) =>
            !string.IsNullOrWhiteSpace(category) && All.ContainsKey(category);
    }
}
