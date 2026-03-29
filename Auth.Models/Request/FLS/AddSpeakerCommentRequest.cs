using System.ComponentModel.DataAnnotations;

namespace Auth.Models.Request.FLS
{
    public class AddSpeakerCommentRequest
    {
        [Required]
        public int FLSDocumentId { get; set; }

        [Required]
        public bool IsApproval { get; set; }

        /// <summary>Max 500 words enforced server-side</summary>
        [Required, MaxLength(3500)]
        public string Content { get; set; } = string.Empty;
    }
}
