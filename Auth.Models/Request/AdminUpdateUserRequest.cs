namespace Auth.Models.Request
{
    /// <summary>
    /// Fields an administrator may correct on someone else's account. Email is included
    /// here, unlike self-service, because a mistyped address at intake otherwise leaves the
    /// person unable to receive a reset link at all.
    /// </summary>
    public class AdminUpdateUserRequest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
    }
}
