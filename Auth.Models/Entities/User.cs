using Microsoft.AspNetCore.Identity;

namespace Auth.Models.Entities
{
    public class User : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Title { get; set; }

        // Mentor relationship
        public string? MentorId { get; set; }
        public User? Mentor { get; set; }

        // Mentor → many mentees
        public ICollection<User> Scholars { get; set; } = new List<User>();

        public DateTime? UpdatedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public bool MustChangePassword { get; set; }
    }
}
