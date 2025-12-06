namespace Auth.Services.Interfaces
{
    public interface IResendService
    {
        Task SendEmailAsync(string to, string subject, string htmlBody);
    }
}
