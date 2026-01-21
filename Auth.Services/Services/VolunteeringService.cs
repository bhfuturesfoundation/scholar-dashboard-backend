using Auth.Models.DTOs;
using Auth.Services.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Auth.Services.Services
{
    public class VolunteeringService : IVolunteeringService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<VolunteeringService> _logger;

        public VolunteeringService(
            IConfiguration configuration,
            ILogger<VolunteeringService> logger)
        {
            _configuration = configuration;
            _logger = logger;
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

                var spreadsheetId = _configuration["SPREADSHEET_ID"];

                if (string.IsNullOrEmpty(spreadsheetId))
                {
                    _logger.LogError("SPREADSHEET_ID is not configured");
                    throw new Exception("Spreadsheet ID is not configured");
                }

                _logger.LogInformation("Attempting to read team data from spreadsheet: {SpreadsheetId}",
                    spreadsheetId);

                // Read from "Long-term Volunteering" sheet, columns A to C
                var range = "Long-term Volunteering!A2:C";

                _logger.LogInformation("Reading from range: {Range}", range);

                var request = service.Spreadsheets.Values.Get(spreadsheetId, range);
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

            var spreadsheetId = _configuration["SPREADSHEET_ID"];

            if (string.IsNullOrEmpty(spreadsheetId))
            {
                _logger.LogError("SPREADSHEET_ID is not configured");
                throw new Exception("Spreadsheet ID is not configured");
            }

            _logger.LogInformation("Attempting to read from spreadsheet: {SpreadsheetId}", spreadsheetId);

            // Get spreadsheet metadata to find sheet names
            var metadataRequest = service.Spreadsheets.Get(spreadsheetId);
            var metadata = await metadataRequest.ExecuteAsync();

            // Use the FIRST sheet's actual name
            var firstSheetName = metadata.Sheets[0].Properties.Title;
            var range = $"{firstSheetName}!A2:H";

            _logger.LogInformation("Reading from sheet: '{SheetName}', Range: {Range}",
                firstSheetName, range);

            var request = service.Spreadsheets.Values.Get(spreadsheetId, range);
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
            var credentialsJson = _configuration["GOOGLE_CREDENTIALS"];

            if (!string.IsNullOrEmpty(credentialsJson))
            {
                _logger.LogInformation("Loading credentials from environment variable");
                var credentialBytes = System.Text.Encoding.UTF8.GetBytes(credentialsJson);
                using var stream = new MemoryStream(credentialBytes);
                return GoogleCredential.FromStream(stream)
                    .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
            }
            else
            {
                var credentialPath = Path.Combine(Directory.GetCurrentDirectory(), "credentials.json");

                _logger.LogInformation("Loading credentials from file: {Path}", credentialPath);

                if (!File.Exists(credentialPath))
                {
                    _logger.LogError("Credentials file not found at: {Path}", credentialPath);
                    throw new FileNotFoundException("Google credentials file not found", credentialPath);
                }

                using var stream = new FileStream(credentialPath, FileMode.Open, FileAccess.Read);
                return GoogleCredential.FromStream(stream)
                    .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
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