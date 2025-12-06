using Auth.Services.Interfaces;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Auth.Services.Services
{
    public class ResendEmailService : IResendService
    {
        private readonly HttpClient _httpClient;
        private readonly string _serviceId;
        private readonly string _templateId;
        private readonly string _publicKey;

        public ResendEmailService()
        {
            _httpClient = new HttpClient();

            _serviceId = Environment.GetEnvironmentVariable("EMAILJS_SERVICE_ID")
                ?? throw new Exception("EMAILJS_SERVICE_ID is not set in .env");
            _templateId = Environment.GetEnvironmentVariable("EMAILJS_TEMPLATE_ID")
                ?? throw new Exception("EMAILJS_TEMPLATE_ID is not set in .env");
            _publicKey = Environment.GetEnvironmentVariable("EMAILJS_PUBLIC_KEY")
                ?? throw new Exception("EMAILJS_PUBLIC_KEY is not set in .env");
        }

        public async Task SendEmailAsync(string to, string subject, string htmlBody)
        {
            var payload = new
            {
                service_id = _serviceId,
                template_id = _templateId,
                user_id = _publicKey,
                template_params = new
                {
                    to_email = to,
                    subject = subject,
                    html_body = htmlBody
                }
            };

            var response = await _httpClient.PostAsJsonAsync("https://api.emailjs.com/api/v1.0/email/send", payload);

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to send email via EmailJS: {content}");
            }
        }
    }
}
