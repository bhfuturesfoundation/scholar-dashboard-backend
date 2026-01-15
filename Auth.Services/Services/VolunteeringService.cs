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

                // Log available sheets for debugging
                var sheetNames = string.Join(", ", metadata.Sheets.Select(s => s.Properties.Title));
                _logger.LogInformation("Available sheets: {Sheets}", sheetNames);

                // Use the FIRST sheet's actual name
                var firstSheetName = metadata.Sheets[0].Properties.Title;

                // Updated range to get columns A through H (all your data)
                var range = $"{firstSheetName}!A2:H";

                _logger.LogInformation("Reading from sheet: '{SheetName}', Range: {Range}",
                    firstSheetName, range);

                var request = service.Spreadsheets.Values.Get(spreadsheetId, range);
                var response = await request.ExecuteAsync();
                var values = response.Values;

                if (values == null || values.Count == 0)
                {
                    _logger.LogWarning("No data found in range {Range}", range);
                    return Enumerable.Empty<VolunteerDto>();
                }

                _logger.LogInformation("Found {RowCount} rows of data", values.Count);

                var volunteers = ParseVolunteersFromSheet(values);

                // Get top 3 by total volunteering hours (Column H)
                var topVolunteers = volunteers
                    .OrderByDescending(v => v.TotalVolunteeringHours)
                    .Take(3)
                    .ToList();

                _logger.LogInformation("Successfully fetched {Count} top volunteers", topVolunteers.Count);

                return topVolunteers;
            }
            catch (Google.GoogleApiException gex)
            {
                _logger.LogError(gex,
                    "Google API Error: {Message}. Status: {Status}. Errors: {Errors}",
                    gex.Message,
                    gex.HttpStatusCode,
                    gex.Error?.Errors != null
                        ? string.Join(", ", gex.Error.Errors.Select(e => $"{e.Reason}: {e.Message}"))
                        : "No additional error details");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading data from Google Sheets");
                throw;
            }
        }

        private GoogleCredential GetGoogleCredential()
        {
            var credentialsJson = _configuration["GOOGLE_CREDENTIALS"];

            if (!string.IsNullOrEmpty(credentialsJson))
            {
                // Load from environment variable (production)
                _logger.LogInformation("Loading credentials from environment variable");
                var credentialBytes = System.Text.Encoding.UTF8.GetBytes(credentialsJson);
                using var stream = new MemoryStream(credentialBytes);
                return GoogleCredential.FromStream(stream)
                    .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
            }
            else
            {
                // Load from file (development)
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
                // Skip rows that don't have enough columns (need at least column H = index 7)
                if (row.Count < 8)
                {
                    _logger.LogWarning("Skipping row with insufficient columns: {ColumnCount}", row.Count);
                    continue;
                }

                // Column A: Number (index 0) - skip it
                // Column B: Full Name (index 1)
                var fullName = row[1]?.ToString()?.Trim() ?? string.Empty;

                // Split name into first and last name
                var nameParts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var firstName = nameParts.Length > 0 ? nameParts[0] : string.Empty;
                var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : string.Empty;

                // Column C: Member Status (index 2)
                var memberStatus = row[2]?.ToString()?.Trim() ?? string.Empty;

                // Column D: Short-term VOs (index 3)
                var shortTermHours = ParseInt(row[3]?.ToString());

                // Column E: Long-term VOs (index 4)
                var longTermHours = ParseInt(row[4]?.ToString());

                // Column F: Outside BHFF (index 5)
                var outsideHours = ParseInt(row[5]?.ToString());

                // Column G: Within BHFF (index 6)
                var withinHours = ParseInt(row[6]?.ToString());

                // Column H: Total hours 2023/2024 (index 7)
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

        private int ParseInt(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            // Remove any whitespace
            value = value.Trim();

            if (int.TryParse(value, out int result))
                return result;

            _logger.LogWarning("Failed to parse integer value: '{Value}', defaulting to 0", value);
            return 0;
        }
    }
}