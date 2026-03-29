using Auth.Models.Enums.FLS;
using System.ComponentModel.DataAnnotations;

namespace Auth.Models.Request.FLS
{
    public class CreateSpeakerTaskRequest
    {
        [Required]
        public int SpeakerProfileId { get; set; }

        [Required]
        public SpeakerTaskType TaskType { get; set; }

        [Required, MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Description { get; set; }

        [MaxLength(300)]
        public string? Location { get; set; }

        public DateTime? TaskStartTime { get; set; }
        public DateTime? TaskEndTime { get; set; }
    }

    public class UpdateSpeakerTaskRequest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public DateTime? TaskStartTime { get; set; }
        public DateTime? TaskEndTime { get; set; }
        public bool? IsCompleted { get; set; }
    }
}
