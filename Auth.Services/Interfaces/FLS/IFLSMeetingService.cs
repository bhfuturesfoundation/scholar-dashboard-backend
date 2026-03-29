using Auth.Models.DTOs.FLS;
using Auth.Models.Request.FLS;

namespace Auth.Services.Interfaces.FLS
{
    public interface IFLSMeetingService
    {
        Task<MeetingSlotDto> CreateSlotAsync(CreateMeetingSlotRequest request);
        Task<List<MeetingSlotDto>> GetAvailableSlotsAsync();
        Task<List<MeetingSlotDto>> GetAllSlotsAsync();
        Task<List<MeetingSlotDto>> GetSlotsForSpeakerAsync(int speakerProfileId);
        Task<MeetingSlotDto> BookSlotAsync(int slotId, int speakerProfileId, BookMeetingSlotRequest request);
        Task<bool> CancelBookingAsync(int slotId, int speakerProfileId);
        Task<bool> DeleteSlotAsync(int slotId);
        Task<bool> HasConflictAsync(DateTime startTime, DateTime endTime, int? excludeSlotId = null);
    }
}
