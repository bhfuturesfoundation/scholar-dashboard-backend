using Auth.Models.Enums.FLS;

namespace Auth.Models.Request.FLS
{
    public class VerifyUploadRequest
    {
        public UploadStatus Status { get; set; }
        public string? VerificationNotes { get; set; }
    }
}
