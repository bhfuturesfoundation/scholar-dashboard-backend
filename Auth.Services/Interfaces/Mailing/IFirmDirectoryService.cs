using Auth.Models.DTOs.Mailing;
using Auth.Models.Enums.Mailing;
using Auth.Models.Request.Mailing;
using Auth.Models.Response;

namespace Auth.Services.Interfaces.Mailing
{
    public class FirmQuery
    {
        public string? Search { get; set; }
        public int? FirmTypeId { get; set; }
        public int? FirmGroupId { get; set; }
        public FirmStatus? Status { get; set; }

        /// <summary>Filter to firms with, or without, a usable contact name.</summary>
        public bool? HasContactName { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
    }

    /// <summary>CRUD and bulk operations over the firm outreach directory.</summary>
    public interface IFirmDirectoryService
    {
        Task<PagedResult<FirmDto>> SearchAsync(FirmQuery query, CancellationToken cancellationToken = default);
        Task<FirmDto?> GetAsync(int id, CancellationToken cancellationToken = default);
        Task<FirmDto> CreateAsync(UpsertFirmRequest request, string? userId, CancellationToken cancellationToken = default);
        Task<FirmDto> UpdateAsync(int id, UpsertFirmRequest request, CancellationToken cancellationToken = default);
        Task DeleteAsync(int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Proposes contact names without saving. The two-step propose/apply flow exists
        /// because a wrong name is sent to a real potential sponsor — the operator sees every
        /// suggestion, its confidence and its reasoning before anything is written.
        /// </summary>
        Task<List<NameDetectionResultDto>> DetectNamesAsync(
            DetectNamesRequest request, CancellationToken cancellationToken = default);

        /// <summary>Saves reviewed names. Returns how many firms were updated.</summary>
        Task<int> ApplyNamesAsync(ApplyNamesRequest request, CancellationToken cancellationToken = default);

        /// <summary>Auto-assigns firm types from keywords. Returns how many were categorised.</summary>
        Task<int> BulkCategorizeAsync(BulkCategorizeRequest request, CancellationToken cancellationToken = default);

        /// <summary>Sets status on many firms at once — the bulk unsubscribe / do-not-contact action.</summary>
        Task<int> BulkSetStatusAsync(List<int> firmIds, FirmStatus status, CancellationToken cancellationToken = default);
    }
}
