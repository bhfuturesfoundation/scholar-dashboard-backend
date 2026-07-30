using Auth.Models.DTOs.Mailing;
using Auth.Models.Entities.Mailing;
using Auth.Models.Request.Mailing;

namespace Auth.Services.Interfaces.Mailing
{
    /// <summary>Templates plus the campaigns that send them.</summary>
    public interface IMailingCampaignService
    {
        // ── Templates ─────────────────────────────────────────────────────────

        Task<List<MailingTemplateDto>> GetTemplatesAsync(bool includeInactive = false, CancellationToken cancellationToken = default);
        Task<MailingTemplateDto?> GetTemplateAsync(int id, CancellationToken cancellationToken = default);
        Task<MailingTemplateDto> CreateTemplateAsync(UpsertTemplateRequest request, string? userId, CancellationToken cancellationToken = default);
        Task<MailingTemplateDto> UpdateTemplateAsync(int id, UpsertTemplateRequest request, CancellationToken cancellationToken = default);
        Task DeleteTemplateAsync(int id, CancellationToken cancellationToken = default);

        // ── Audience ──────────────────────────────────────────────────────────

        /// <summary>Resolves an audience selection to the firms it currently matches.</summary>
        Task<List<Firm>> ResolveAudienceAsync(AudienceSelection selection, CancellationToken cancellationToken = default);

        /// <summary>
        /// Renders the campaign without sending, reporting how many firms get which variant,
        /// which are suppressed, and any placeholder left unresolved.
        ///
        /// Sending is blocked while unresolved placeholders remain — shipping "Dear
        /// {{firstName}}" to a few hundred potential sponsors is the exact failure this
        /// whole flow exists to prevent.
        /// </summary>
        Task<CampaignPreviewDto> PreviewAsync(PreviewCampaignRequest request, CancellationToken cancellationToken = default);

        // ── Sending ───────────────────────────────────────────────────────────

        Task<MailingCampaignDto> SendAsync(
            SendMailingCampaignRequest request, string userId, string userName, CancellationToken cancellationToken = default);

        Task<List<MailingCampaignDto>> GetCampaignsAsync(int limit = 50, CancellationToken cancellationToken = default);
        Task<MailingCampaignDto?> GetCampaignAsync(int id, CancellationToken cancellationToken = default);
        Task<List<MailingCampaignRecipientDto>> GetRecipientsAsync(int campaignId, CancellationToken cancellationToken = default);

        /// <summary>Retries only the recipients that failed. Suppressed ones are not retried.</summary>
        Task<MailingCampaignDto> RetryFailedAsync(int campaignId, CancellationToken cancellationToken = default);
    }
}
