namespace Auth.Models.DTOs.Settings
{
    public class AppSettingDto
    {
        public string Key { get; set; } = string.Empty;

        /// <summary>The value the app is using right now.</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>"app" (saved here), "server" (environment variable) or "default".</summary>
        public string Source { get; set; } = "default";

        public string DefaultValue { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
    }

    public class UpdateAppSettingsRequest
    {
        /// <summary>Key → new value. An empty value removes the override.</summary>
        public Dictionary<string, string?> Values { get; set; } = new();
    }
}
