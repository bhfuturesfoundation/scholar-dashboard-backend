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

    public class AdminCreateUserRequest
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public List<string> Roles { get; set; } = new();

        /// <summary>Scholars only. Defaults to the current generation.</summary>
        public int? GenerationId { get; set; }

        /// <summary>Scholars only. Defaults to Junior.</summary>
        public Enums.Scholars.ScholarStatus? ScholarStatus { get; set; }

        public bool SendWelcomeEmail { get; set; } = true;
    }

    public class DeleteAccountsRequest
    {
        public List<string> UserIds { get; set; } = new();
    }
}
