using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Auth.Models.Entities
{
    public class RefreshToken
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Token { get; set; }

        [Required]
        public DateTime ExpiryTime { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        [Required]
        public string UserId { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; }

        public bool IsExpired => DateTime.UtcNow >= ExpiryTime;

        public string? ReplacedByToken { get; set; }

        public DateTime? RevokedAt { get; set; }

        public int RefreshCount { get; set; } = 0;

        public string? RevokeReason { get; set; }


        // Additional properties for tracking
        public string? CreatedByIp { get; set; }
        public string? RevokedByIp { get; set; }
        public string? DeviceInfo { get; set; }

        /// <summary>
        /// Hard end of the sign-in session this token belongs to, set at login and inherited
        /// unchanged by every rotated token. <see cref="ExpiryTime"/> slides forward on each
        /// rotation; without a fixed deadline an open tab renews itself forever and the user
        /// is never asked to sign in again. Null only on tokens issued before this existed.
        /// </summary>
        public DateTime? SessionExpiresAt { get; set; }
    }
}
