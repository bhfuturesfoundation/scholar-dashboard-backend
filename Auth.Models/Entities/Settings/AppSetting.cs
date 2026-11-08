namespace Auth.Models.Entities.Settings
{
    /// <summary>
    /// A setting changed from Admin → App settings. A row overrides the environment
    /// variable of the same name; deleting the row falls back to it.
    /// </summary>
    public class AppSetting
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedByUserId { get; set; }
    }
}
