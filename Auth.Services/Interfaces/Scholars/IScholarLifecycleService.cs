using Auth.Models.DTOs.Scholars;
using Auth.Models.Enums.Scholars;
using Auth.Models.Request.Scholars;
using Auth.Services.Services.Operations;

namespace Auth.Services.Interfaces.Scholars
{
    /// <summary>
    /// Generations, cohort promotion and bulk intake.
    ///
    /// Every mutating operation here touches many accounts at once, so each is
    /// preview-then-apply and leaves an audit trail. Promotion additionally records enough
    /// to be undone — a mis-run promotion is not something anyone fixes by hand across a few
    /// hundred scholars.
    /// </summary>
    public interface IScholarLifecycleService
    {
        // ── Generations ───────────────────────────────────────────────────────

        Task<List<ScholarGenerationDto>> GetGenerationsAsync(CancellationToken cancellationToken = default);

        Task<ScholarGenerationDto> CreateGenerationAsync(
            UpsertGenerationRequest request, string? userId, CancellationToken cancellationToken = default);

        Task<ScholarGenerationDto> UpdateGenerationAsync(
            int id, UpsertGenerationRequest request, CancellationToken cancellationToken = default);

        /// <summary>Marks one generation as the default for new intake, clearing the previous.</summary>
        Task SetCurrentGenerationAsync(int id, CancellationToken cancellationToken = default);

        Task DeleteGenerationAsync(int id, CancellationToken cancellationToken = default);

        // ── Overview ──────────────────────────────────────────────────────────

        /// <summary>Counts by status and generation, including the unassigned bucket.</summary>
        Task<ScholarOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);

        // ── Promotion ─────────────────────────────────────────────────────────

        /// <summary>
        /// Who a promotion would move, without moving them. The UI requires this before the
        /// apply button enables.
        /// </summary>
        Task<PromotionPreviewDto> PreviewPromotionAsync(
            PromotionRequest request, CancellationToken cancellationToken = default);

        Task<PromotionResultDto> ApplyPromotionAsync(
            PromotionRequest request, string userId, string userName, CancellationToken cancellationToken = default);

        Task<List<PromotionBatchDto>> GetPromotionHistoryAsync(
            int limit = 25, CancellationToken cancellationToken = default);

        /// <summary>Restores every account in a batch to the status and active flag it had before.</summary>
        Task<PromotionResultDto> RevertPromotionAsync(
            int batchId, string userId, CancellationToken cancellationToken = default);

        /// <summary>Sets status on a hand-picked set — for fixing up the unassigned bucket.</summary>
        Task<int> SetStatusAsync(
            List<string> userIds, ScholarStatus status, int? generationId, CancellationToken cancellationToken = default);

        // ── Bulk intake ───────────────────────────────────────────────────────

        /// <summary>
        /// Creates scholar accounts from a spreadsheet. Dry-run by default.
        ///
        /// Generated passwords are returned in the result exactly once, because they are not
        /// recoverable afterwards — only the hash is stored. The caller is responsible for
        /// handing them over and not keeping them.
        /// </summary>
        Task<ScholarImportResultDto> ImportScholarsAsync(
            Stream fileStream,
            string fileName,
            ScholarImportOptions options,
            string? userId,
            CancellationToken cancellationToken = default);

        /// <summary>A blank sheet with the expected headers and an example row.</summary>
        ExportTable BuildImportTemplate();
    }
}
