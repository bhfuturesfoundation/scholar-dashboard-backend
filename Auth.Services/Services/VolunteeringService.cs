using Auth.Models.DTOs;
using Auth.Services.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Auth.Services.Services
{
    public class VolunteeringService : IVolunteeringService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<VolunteeringService> _logger;
        private readonly string _spreadsheetId;
        private readonly string? _googleCredentials;

        public VolunteeringService(
            IConfiguration configuration,
            ILogger<VolunteeringService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            // Load from environment variables
            _spreadsheetId = Environment.GetEnvironmentVariable("SPREADSHEET_ID")
                ?? throw new Exception("SPREADSHEET_ID is not set in .env");

            // Google credentials is optional - can fall back to credentials.json
            _googleCredentials = Environment.GetEnvironmentVariable("GOOGLE_CREDENTIALS");
        }

        public async Task<IEnumerable<VolunteerDto>> GetTopVolunteersAsync()
        {
            try
            {
                var allVolunteers = await GetAllVolunteersFromSheetAsync();

                // Get top 3 by total volunteering hours
                var topVolunteers = allVolunteers
                    .OrderByDescending(v => v.TotalVolunteeringHours)
                    .Take(3)
                    .ToList();

                _logger.LogInformation("Successfully fetched {Count} top volunteers", topVolunteers.Count);

                return topVolunteers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching top volunteers");
                throw;
            }
        }

        public async Task<IEnumerable<VolunteerDto>> GetAllVolunteersAsync()
        {
            try
            {
                var allVolunteers = await GetAllVolunteersFromSheetAsync();

                // Return ALL volunteers sorted by total hours (descending)
                var sortedVolunteers = allVolunteers
                    .OrderByDescending(v => v.TotalVolunteeringHours)
                    .ToList();

                _logger.LogInformation("Successfully fetched {Count} volunteers (all volunteers)",
                    sortedVolunteers.Count);

                return sortedVolunteers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all volunteers");
                throw;
            }
        }

        public async Task<IEnumerable<VolunteerWithTeamDto>> GetVolunteersWithTeamLeadersAsync()
        {
            try
            {
                var credential = GetGoogleCredential();
                var service = CreateSheetsService(credential);

                _logger.LogInformation("Attempting to read team data from spreadsheet: {SpreadsheetId}",
                    _spreadsheetId);

                // Read from "Long-term Volunteering" sheet, columns A to C
                var range = "Long-term Volunteering!A2:C";

                _logger.LogInformation("Reading from range: {Range}", range);

                var request = service.Spreadsheets.Values.Get(_spreadsheetId, range);
                var response = await request.ExecuteAsync();
                var values = response.Values;

                if (values == null || values.Count == 0)
                {
                    _logger.LogWarning("No data found in Long-term Volunteering sheet");
                    return Enumerable.Empty<VolunteerWithTeamDto>();
                }

                _logger.LogInformation("Found {RowCount} rows of team data", values.Count);

                var volunteersWithTeams = ParseVolunteersWithTeams(values);

                _logger.LogInformation("Successfully fetched {Count} volunteers with team leaders",
                    volunteersWithTeams.Count);

                return volunteersWithTeams;
            }
            catch (Google.GoogleApiException gex)
            {
                _logger.LogError(gex,
                    "Google API Error: {Message}. Status: {Status}",
                    gex.Message,
                    gex.HttpStatusCode);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading team data from Google Sheets");
                throw;
            }
        }

        // Private helper method to get all volunteers from main sheet
        private async Task<List<VolunteerDto>> GetAllVolunteersFromSheetAsync()
        {
            var credential = GetGoogleCredential();
            var service = CreateSheetsService(credential);

            _logger.LogInformation("Attempting to read from spreadsheet: {SpreadsheetId}", _spreadsheetId);

            // Get spreadsheet metadata to find sheet names
            var metadataRequest = service.Spreadsheets.Get(_spreadsheetId);
            var metadata = await metadataRequest.ExecuteAsync();

            // Use the FIRST sheet's actual name
            var firstSheetName = metadata.Sheets[0].Properties.Title;
            var range = $"{firstSheetName}!A2:H";

            _logger.LogInformation("Reading from sheet: '{SheetName}', Range: {Range}",
                firstSheetName, range);

            var request = service.Spreadsheets.Values.Get(_spreadsheetId, range);
            var response = await request.ExecuteAsync();
            var values = response.Values;

            if (values == null || values.Count == 0)
            {
                _logger.LogWarning("No data found in range {Range}", range);
                return new List<VolunteerDto>();
            }

            _logger.LogInformation("Found {RowCount} rows of data", values.Count);

            return ParseVolunteersFromSheet(values);
        }

        private GoogleCredential GetGoogleCredential()
        {
            // PRIORITY 1: Try to load from environment variable (PRODUCTION)
            if (!string.IsNullOrEmpty(_googleCredentials))
            {
                _logger.LogInformation("Loading Google credentials from environment variable");

                try
                {
                    // Strip outer quotes if they were included in the environment variable value
                    string cleanedCredentials = _googleCredentials.Trim();

                    if ((cleanedCredentials.StartsWith("'") && cleanedCredentials.EndsWith("'")) ||
                        (cleanedCredentials.StartsWith("\"") && cleanedCredentials.EndsWith("\"")))
                    {
                        _logger.LogWarning("Removing outer quotes from GOOGLE_CREDENTIALS. " +
                            "Fix your deployment config to not include quotes in the value.");
                        cleanedCredentials = cleanedCredentials.Substring(1, cleanedCredentials.Length - 2);
                    }

                    _logger.LogInformation("Credentials: {Length} chars, starts with '{FirstChar}'",
                        cleanedCredentials.Length,
                        cleanedCredentials.Length > 0 ? cleanedCredentials[0].ToString() : "empty");

                    // Basic validation
                    if (!cleanedCredentials.TrimStart().StartsWith("{"))
                    {
                        var preview = cleanedCredentials.Substring(0, Math.Min(100, cleanedCredentials.Length));
                        _logger.LogError("Credentials don't start with '{{'. Preview: {Preview}", preview);
                        throw new Exception("GOOGLE_CREDENTIALS doesn't appear to be valid JSON");
                    }

                    // KEY INSIGHT: Don't modify the JSON!
                    // Google's FromStream() expects standard JSON with escaped \n
                    // The library will handle the conversion internally

                    var credentialBytes = Encoding.UTF8.GetBytes(cleanedCredentials);
                    using var stream = new MemoryStream(credentialBytes);

                    _logger.LogInformation("Calling GoogleCredential.FromStream()...");
                    var credential = GoogleCredential.FromStream(stream)
                        .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);

                    _logger.LogInformation("✓ Successfully loaded credentials from environment variable");
                    return credential;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load credentials from environment variable");
                    _logger.LogError("Error: {Message}", ex.Message);
                    if (ex.InnerException != null)
                    {
                        _logger.LogError("Inner exception: {InnerMessage}", ex.InnerException.Message);
                    }
                    throw new Exception($"Invalid GOOGLE_CREDENTIALS: {ex.Message}", ex);
                }
            }

            // PRIORITY 2: Fall back to credentials.json file (DEVELOPMENT)
            _logger.LogInformation("GOOGLE_CREDENTIALS not set, falling back to credentials.json");

            var credentialPath = Path.Combine(Directory.GetCurrentDirectory(), "credentials.json");

            if (!File.Exists(credentialPath))
            {
                _logger.LogError("credentials.json not found at: {Path}", credentialPath);
                throw new FileNotFoundException(
                    "GOOGLE_CREDENTIALS not set and credentials.json not found", credentialPath);
            }

            _logger.LogInformation("Loading credentials from file: {Path}", credentialPath);

            try
            {
                using var stream = new FileStream(credentialPath, FileMode.Open, FileAccess.Read);
                var credential = GoogleCredential.FromStream(stream)
                    .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
                _logger.LogInformation("✓ Loaded credentials from file");
                return credential;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load credentials from file");
                throw new Exception($"Failed to load {credentialPath}: {ex.Message}", ex);
            }
        }

        private SheetsService CreateSheetsService(GoogleCredential credential)
        {
            var appName = _configuration["GoogleSheets:ApplicationName"] ?? "VolunteerLeaderboard";

            return new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = appName
            });
        }

        private List<VolunteerDto> ParseVolunteersFromSheet(IList<IList<object>> values)
        {
            var volunteers = new List<VolunteerDto>();

            foreach (var row in values)
            {
                if (row.Count < 8)
                {
                    _logger.LogWarning("Skipping row with insufficient columns: {ColumnCount}", row.Count);
                    continue;
                }

                var fullName = row[1]?.ToString()?.Trim() ?? string.Empty;
                var nameParts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var firstName = nameParts.Length > 0 ? nameParts[0] : string.Empty;
                var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : string.Empty;

                var memberStatus = row[2]?.ToString()?.Trim() ?? string.Empty;
                var shortTermHours = ParseInt(row[3]?.ToString());
                var longTermHours = ParseInt(row[4]?.ToString());
                var outsideHours = ParseInt(row[5]?.ToString());
                var withinHours = ParseInt(row[6]?.ToString());
                var totalHours = ParseInt(row[7]?.ToString());

                volunteers.Add(new VolunteerDto
                {
                    Name = firstName,
                    Surname = lastName,
                    MemberStatus = memberStatus,
                    ShortTermHours = shortTermHours,
                    LongTermHours = longTermHours,
                    OutsideBHFFHours = outsideHours,
                    WithinBHFFHours = withinHours,
                    TotalVolunteeringHours = totalHours
                });
            }

            return volunteers;
        }

        private List<VolunteerWithTeamDto> ParseVolunteersWithTeams(IList<IList<object>> values)
        {
            var volunteersWithTeams = new List<VolunteerWithTeamDto>();
            var teamLeaders = new Dictionary<string, string>(); // team -> leader name

            // First pass: identify team leaders
            foreach (var row in values)
            {
                if (row.Count < 3) continue;

                var team = row[0]?.ToString()?.Trim() ?? string.Empty;
                var placementType = row[1]?.ToString()?.Trim() ?? string.Empty;
                var fullName = row[2]?.ToString()?.Trim() ?? string.Empty;

                if (placementType.Equals("Team leader", StringComparison.OrdinalIgnoreCase))
                {
                    teamLeaders[team] = fullName;
                }
            }

            // Second pass: build volunteer list with team leaders
            foreach (var row in values)
            {
                if (row.Count < 3)
                {
                    _logger.LogWarning("Skipping row with insufficient columns: {ColumnCount}", row.Count);
                    continue;
                }

                var team = row[0]?.ToString()?.Trim() ?? string.Empty;
                var placementType = row[1]?.ToString()?.Trim() ?? string.Empty;
                var fullName = row[2]?.ToString()?.Trim() ?? string.Empty;

                var nameParts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var firstName = nameParts.Length > 0 ? nameParts[0] : string.Empty;
                var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : string.Empty;

                // Get team leader for this team
                var teamLeader = teamLeaders.ContainsKey(team) ? teamLeaders[team] : "No Team Leader";

                volunteersWithTeams.Add(new VolunteerWithTeamDto
                {
                    Name = firstName,
                    Surname = lastName,
                    Team = team,
                    PlacementType = placementType,
                    TeamLeader = teamLeader
                });
            }

            return volunteersWithTeams;
        }

        private int ParseInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            value = value.Trim();

            if (int.TryParse(value, out int result))
                return result;

            _logger.LogWarning("Failed to parse integer value: '{Value}', defaulting to 0", value);
            return 0;
        }
    }
}