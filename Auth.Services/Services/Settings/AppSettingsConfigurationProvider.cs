using Microsoft.Extensions.Configuration;

namespace Auth.Services.Services.Settings
{
    /// <summary>
    /// Feeds settings saved in the app into <see cref="IConfiguration"/>.
    ///
    /// Added after the environment variables, so a saved value wins and a removed one falls
    /// back to the server's. That lets the services keep reading
    /// <c>_configuration["JOURNAL_WINDOW_CLOSE_DAY"]</c> as they always have, with no idea
    /// where the value came from. The data is replaced in memory on every save; on more
    /// than one instance, the others pick it up at their next restart.
    /// </summary>
    public sealed class AppSettingsConfigurationProvider : ConfigurationProvider, IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

        public void Replace(IDictionary<string, string> values)
        {
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in values) Data[key] = value;
            OnReload();
        }

        public bool Has(string key) => Data.ContainsKey(key);
    }
}
