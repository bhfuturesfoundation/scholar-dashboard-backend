namespace Auth.Services.Settings
{
    public class SMTPSettings
    {
        public string Host { get; set; } = null!;
        public int Port { get; set; }
        public string Username { get; set; } = null!;
        public string Password { get; set; } = null!;
        public bool EnableSsl { get; set; }
        public string FromEmail { get; set; } = null!;
        public string FromName { get; set; } = null!;

        /// <summary>
        /// SMTP_ENABLED. Defaults to true so an existing deployment that never set it keeps
        /// working; setting it to false now actually disables the provider, which it did not
        /// before — the variable was present in configuration and read nowhere.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}