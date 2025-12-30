using CsvHelper.Configuration.Attributes;

namespace Auth.Services.Services.Seed
{
    public class MentorCsvRecord
    {
        [Name("Mentor name")]
        public string MentorName { get; set; }

        [Name("Mentor email")]
        public string MentorEmail { get; set; }

        [Name("Scholar")]
        public string Scholar { get; set; }

        [Name("Scholar email")]
        public string ScholarEmail { get; set; }

        [Name("BHFF status")]
        public string BHFFStatus { get; set; }

        // Split mentor name automatically
        public string MentorFirstName => string.IsNullOrEmpty(MentorName) ? "" : MentorName.Split(" ")[0];
        public string MentorLastName => string.IsNullOrEmpty(MentorName) ? "" : string.Join(" ", MentorName.Split(" ").Skip(1));
    }
}
