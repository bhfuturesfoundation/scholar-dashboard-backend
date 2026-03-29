using Auth.Models.DTOs.FLS;
using Auth.Models.Request.FLS;

namespace Auth.Services.Interfaces.FLS
{
    public interface IFLSTaskService
    {
        Task<SpeakerTaskDto> CreateTaskAsync(CreateSpeakerTaskRequest request);
        Task<List<SpeakerTaskDto>> GetTasksForSpeakerAsync(int speakerProfileId);
        Task<SpeakerTaskDto> UpdateTaskAsync(int taskId, UpdateSpeakerTaskRequest request);
        Task<bool> DeleteTaskAsync(int taskId);
    }
}
