using Auth.Models.DTOs.FLS;

namespace Auth.Services.Interfaces.FLS
{
    public interface IFLSAdminService
    {
        Task<List<SpeakerOverviewDto>> GetAllSpeakersAsync(bool includeDeregistered = false);
        Task<SpeakerProfileDto?> GetSpeakerDetailsAsync(int speakerProfileId);
        Task<byte[]> ExportSpeakersAsCsvAsync();
        Task<List<SpeakerUploadDto>> GetPendingUploadsAsync();
    }
}
