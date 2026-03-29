using Auth.Models.DTOs.FLS;
using Auth.Models.Request.FLS;

namespace Auth.Services.Interfaces.FLS
{
    public interface IFLSNotificationService
    {
        Task<List<SpeakerNotificationDto>> GetNotificationsAsync(int speakerProfileId, bool unreadOnly = false);
        Task<bool> MarkAsReadAsync(int notificationId, int speakerProfileId);
        Task<bool> MarkAllReadAsync(int speakerProfileId);
        Task<int> GetUnreadCountAsync(int speakerProfileId);
        Task SendNotificationAsync(SendNotificationRequest request);
        Task SendReminderEmailsAsync();
    }
}
