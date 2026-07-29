using Auth.Models.DTOs.FLS;
using Auth.Models.Request.FLS;

namespace Auth.Services.Interfaces.FLS
{
    /// <summary>
    /// Outbound FLS communications: who can be mailed, what a message will look like,
    /// sending it, and the record of what was sent.
    /// </summary>
    public interface IFLSCampaignService
    {
        /// <summary>Everyone a campaign could address — speakers and FLS staff.</summary>
        Task<List<DirectoryRecipientDto>> GetRecipientDirectoryAsync(CancellationToken cancellationToken = default);

        /// <summary>Providers, defaults and template variables for the settings screen.</summary>
        EmailSettingsDto GetEmailSettings();

        /// <summary>Dry run — resolves the audience and renders the message without sending.</summary>
        Task<CampaignPreviewDto> PreviewAsync(PreviewCampaignRequest request, CancellationToken cancellationToken = default);

        /// <summary>Resolves the audience, sends to each recipient, and records the outcome.</summary>
        Task<EmailCampaignDetailDto> SendAsync(
            SendCampaignRequest request,
            string userId,
            string userName,
            CancellationToken cancellationToken = default);

        Task<List<EmailCampaignSummaryDto>> GetCampaignsAsync(int take = 50, CancellationToken cancellationToken = default);

        Task<EmailCampaignDetailDto?> GetCampaignAsync(int campaignId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Re-sends a completed campaign to the recipients that failed, reusing the original
        /// message. Returns the updated campaign.
        /// </summary>
        Task<EmailCampaignDetailDto> RetryFailedAsync(
            int campaignId,
            string? providerKey,
            CancellationToken cancellationToken = default);

        /// <summary>Sends the composed message to a single address so the sender can eyeball it.</summary>
        Task<bool> SendTestEmailAsync(
            SendCampaignRequest request,
            string toEmail,
            CancellationToken cancellationToken = default);
    }
}
