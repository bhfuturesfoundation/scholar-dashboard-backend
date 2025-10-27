using Microsoft.AspNetCore.Identity;

namespace Auth.Models.Entities
{
    public class User : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Title { get; set; }
        public string? MentorId { get; set; }
        public virtual User? Mentor { get; set; }
        public virtual User? Scholar { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }
    }
}
