using Auth.Models.Enums.FLS;

namespace Auth.Models.DTOs.FLS
{
    public class SpeakerTaskDto
    {
        public int Id { get; set; }
        public int SpeakerProfileId { get; set; }
        public string SpeakerName { get; set; } = string.Empty;
        public SpeakerTaskType TaskType { get; set; }
        public string TaskTypeLabel { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Location { get; set; }
        public DateTime? TaskStartTime { get; set; }
        public DateTime? TaskEndTime { get; set; }
        public bool IsCompleted { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
