namespace Auth.Models.Entities.FLS
{
    public class SpeakerComment
    {
        public int Id { get; set; }
        public int SpeakerProfileId { get; set; }
        public SpeakerProfile SpeakerProfile { get; set; } = null!;

        public int FLSDocumentId { get; set; }
        public FLSDocument FLSDocument { get; set; } = null!;

        /// <summary>True = speaker approved the document, False = speaker requested changes</summary>
        public bool IsApproval { get; set; }

        /// <summary>Max 500 words</summary>
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
