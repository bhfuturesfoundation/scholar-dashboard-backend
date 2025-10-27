namespace Auth.Models.Request
{
    public class UpdateUsersActiveStatusRequest
    {
        public List<string> UserIds { get; set; }
        public bool IsActive { get; set; }
    }
}
