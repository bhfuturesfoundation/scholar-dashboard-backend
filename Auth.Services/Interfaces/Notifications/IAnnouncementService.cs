using Auth.Models.DTOs.Notifications;

namespace Auth.Services.Interfaces.Notifications
{
    /// <summary>
    /// Staff-authored broadcasts.
    ///
    /// Preview-then-send, the same shape as bulk promotion and firm import. A broadcast is
    /// the one action in this system that cannot be taken back — once it is in two hundred
    /// inboxes it is there — so seeing the count and a sample of names first is worth the
    /// extra click.
    /// </summary>
    public interface IAnnouncementService
    {
        /// <summary>
        /// Who this would reach, without sending anything. Applies the same audience filter
        /// and the same per-person preferences the send will, so the email and push counts
        /// are what will actually go out rather than an optimistic total.
        /// </summary>
        Task<AudiencePreviewDto> PreviewAsync(
            AnnouncementRequest request, CancellationToken cancellationToken = default);

        Task<AnnouncementDto> SendAsync(
            AnnouncementRequest request,
            string createdByUserId,
            string createdByName,
            CancellationToken cancellationToken = default);

        Task<List<AnnouncementDto>> GetHistoryAsync(
            int limit = 50, CancellationToken cancellationToken = default);
    }
}
