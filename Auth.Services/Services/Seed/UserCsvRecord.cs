namespace Auth.Services.Services.Seed
{
    public class UserCsvRecord
    {
        public string Email { get; set; }
        public string FullName { get; set; }
        public string ScholarStatus { get; set; }
        public string SoftSkill { get; set; }
        public string HardSkill { get; set; }
        public string InterpersonalSkill { get; set; }
        public string KnowledgeSkill { get; set; }

        // split fullname automatically
        public string FirstName => string.IsNullOrEmpty(FullName) ? "" : FullName.Split(" ")[0];
        public string LastName => string.IsNullOrEmpty(FullName) ? "" : string.Join(" ", FullName.Split(" ").Skip(1));
    }
}
