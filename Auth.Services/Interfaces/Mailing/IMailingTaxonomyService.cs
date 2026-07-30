using Auth.Models.DTOs.Mailing;
using Auth.Models.Request.Mailing;

namespace Auth.Services.Interfaces.Mailing
{
    /// <summary>
    /// Manages the firm taxonomy — groups and types — which the partnerships team edits
    /// themselves. The seeded set is a starting point, not a fixed vocabulary.
    /// </summary>
    public interface IMailingTaxonomyService
    {
        Task<List<FirmGroupDto>> GetGroupsAsync(CancellationToken cancellationToken = default);
        Task<FirmGroupDto> CreateGroupAsync(UpsertFirmGroupRequest request, CancellationToken cancellationToken = default);
        Task<FirmGroupDto> UpdateGroupAsync(int id, UpsertFirmGroupRequest request, CancellationToken cancellationToken = default);
        Task DeleteGroupAsync(int id, CancellationToken cancellationToken = default);

        Task<List<FirmTypeDto>> GetTypesAsync(CancellationToken cancellationToken = default);
        Task<FirmTypeDto> CreateTypeAsync(UpsertFirmTypeRequest request, CancellationToken cancellationToken = default);
        Task<FirmTypeDto> UpdateTypeAsync(int id, UpsertFirmTypeRequest request, CancellationToken cancellationToken = default);
        Task DeleteTypeAsync(int id, CancellationToken cancellationToken = default);
    }
}
