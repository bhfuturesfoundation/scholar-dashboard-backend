using Auth.Models.Enums.FLS;

namespace Auth.Models.Entities.FLS
{
    public class SpeakerTask
    {
        public int Id { get; set; }
        public int SpeakerProfileId { get; set; }
        public SpeakerProfile SpeakerProfile { get; set; } = null!;

        public SpeakerTaskType TaskType { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Location { get; set; }

        public DateTime? TaskStartTime { get; set; }
        public DateTime? TaskEndTime { get; set; }

        public bool IsCompleted { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
