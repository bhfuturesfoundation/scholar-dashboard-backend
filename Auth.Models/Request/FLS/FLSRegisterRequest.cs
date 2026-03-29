using Auth.Models.Enums.FLS;
using System.ComponentModel.DataAnnotations;

namespace Auth.Models.Request.FLS
{
    public class FLSRegisterRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [Compare("Password", ErrorMessage = "Password and confirmation do not match")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        public string? Organization { get; set; }
        public SpeakerType SpeakerType { get; set; } = SpeakerType.Track;
    }
}
