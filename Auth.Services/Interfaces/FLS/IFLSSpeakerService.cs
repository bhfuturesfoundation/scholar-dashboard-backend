using Auth.Models.DTOs.FLS;
using Auth.Models.Request.FLS;

namespace Auth.Services.Interfaces.FLS
{
    public interface IFLSSpeakerService
    {
        Task<SpeakerProfileDto> RegisterSpeakerAsync(FLSRegisterRequest request);
        Task<SpeakerProfileDto?> GetProfileByUserIdAsync(string userId);
        Task<SpeakerProfileDto?> GetProfileByIdAsync(int speakerProfileId);
        Task<SpeakerProfileDto> UpdateProfileAsync(int speakerProfileId, UpdateSpeakerProfileRequest request);
        Task<bool> UpdateAccommodationAsync(int speakerProfileId, UpdateSpeakerAccommodationRequest request);
        Task<bool> DeregisterSpeakerAsync(int speakerProfileId, DeregisterSpeakerRequest request, string adminUserId);
        Task<bool> ReactivateSpeakerAsync(int speakerProfileId);
        Task<bool> SetModificationDeadlineAsync(int speakerProfileId, SetModificationDeadlineRequest request);
        Task<bool> CanModifyUploadsAsync(int speakerProfileId);
    }
}
