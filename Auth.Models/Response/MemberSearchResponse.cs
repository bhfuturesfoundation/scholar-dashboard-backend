namespace Auth.Models.Response
{
    public class MemberSearchResponse
    {
        public string Id { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        /// <summary>Junior or Senior, to tell two people with the same name apart. Emails are never returned.</summary>
        public string Title { get; set; } = string.Empty;
    }
}
