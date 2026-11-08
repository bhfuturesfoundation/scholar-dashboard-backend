namespace Auth.Services.Interfaces
{
    public interface IResendService
    {
        Task SendEmailAsync(string to, string resetLink);

        /// <summary>First email a new account gets: a link to choose their own password.</summary>
        Task SendWelcomeEmailAsync(string to, string? firstName, string setPasswordLink);
    }
}
