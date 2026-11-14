namespace Auth.Models.Response
{
    public class UserWithRolesResponse
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Title { get; set; }
        public string Id { get; set; }
        public string Email { get; set; }
        public List<string> Roles { get; set; }
        public bool IsActive { get; set; }
        public bool IsLockedOut { get; set; }
        public int ScholarStatus { get; set; }
        public DateTime CreatedAt { get; set; }
    }

}
