// One-off import tool for the IPJ intake sheet.
//
// Reads the self-assessment CSV (email, name, scholar status, 4 skill/goal answers with
// their satisfaction ratings) and creates scholar accounts the same way the running app
// would: through UserManager<User>, so passwords are hashed correctly, the "User" role is
// assigned, and nothing bypasses ASP.NET Identity's invariants. Accounts whose email already
// exists are left untouched and reported as skipped — this mirrors the admin intake
// endpoint's own dedup behaviour, so a returning scholar's existing account is never
// silently overwritten by a one-off script.
//
// Defaults to a dry run: connects to the database to validate (existing emails, question
// rows, current generation) but writes nothing. Pass --commit to actually create accounts.
//
// Usage:
//   dotnet run -- <path-to-csv> [--commit] [--out <output-folder>] [--connection-string <url>]
//
// Connection: either a single Postgres URL (postgres://user:pass@host:port/db — this is what
// Railway's "Connect" tab shows, use the PUBLIC one since this tool runs outside Railway's
// private network) via --connection-string or the DATABASE_PUBLIC_URL/DATABASE_URL env vars,
// or the individual PGHOST/PGPORT/PGDATABASE/PGUSER/PGPASSWORD vars the deployed app itself
// reads (see PersistenceServiceExtensions in Auth.API).

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auth.Models.Constants;
using Auth.Models.Data;
using Auth.Models.Entities;
using Auth.Models.Enums.Scholars;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var csvPath = args.FirstOrDefault(a => !a.StartsWith("--"));
var commit = args.Contains("--commit");
var outDirArg = Array.IndexOf(args, "--out");
var outDir = outDirArg >= 0 && outDirArg + 1 < args.Length
    ? args[outDirArg + 1]
    : Path.Combine(AppContext.BaseDirectory, "import-output");

if (csvPath is null || !File.Exists(csvPath))
{
    Console.WriteLine("Usage: dotnet run -- <path-to-csv> [--commit] [--out <output-folder>]");
    Console.WriteLine($"CSV not found: {csvPath}");
    return 1;
}

Directory.CreateDirectory(outDir);

static string? BuildConnectionStringFromUrl(string? url)
{
    if (string.IsNullOrWhiteSpace(url)) return null;

    // Accept both postgres:// and postgresql:// — Railway and most providers use either.
    var normalized = url.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        ? "postgresql://" + url["postgres://".Length..]
        : url;

    var uri = new Uri(normalized);
    var userInfo = uri.UserInfo.Split(':', 2);
    var username = Uri.UnescapeDataString(userInfo[0]);
    var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
    var database = uri.AbsolutePath.TrimStart('/');

    return $"Host={uri.Host};Port={uri.Port};Database={database};Username={username};" +
           $"Password={password};Pooling=true;Trust Server Certificate=true";
}

var connStringArgIdx = Array.IndexOf(args, "--connection-string");
var explicitUrl = connStringArgIdx >= 0 && connStringArgIdx + 1 < args.Length ? args[connStringArgIdx + 1] : null;

var connectionString =
    BuildConnectionStringFromUrl(explicitUrl)
    ?? BuildConnectionStringFromUrl(Environment.GetEnvironmentVariable("DATABASE_PUBLIC_URL"))
    ?? BuildConnectionStringFromUrl(Environment.GetEnvironmentVariable("DATABASE_URL"));

string targetDescription;

if (connectionString is null)
{
    var pgHost = Environment.GetEnvironmentVariable("PGHOST");
    if (string.IsNullOrWhiteSpace(pgHost))
    {
        Console.WriteLine("No connection info found. Pass --connection-string <postgres-url>, or set " +
                           "DATABASE_PUBLIC_URL / DATABASE_URL, or PGHOST+PGPORT+PGDATABASE+PGUSER+PGPASSWORD.");
        return 1;
    }

    var pgPort = Environment.GetEnvironmentVariable("PGPORT") ?? "5432";
    var pgDatabase = Environment.GetEnvironmentVariable("PGDATABASE");
    var pgUser = Environment.GetEnvironmentVariable("PGUSER");
    var pgPassword = Environment.GetEnvironmentVariable("PGPASSWORD");
    connectionString =
        $"Host={pgHost};Port={pgPort};Database={pgDatabase};Username={pgUser};Password={pgPassword};" +
        "Pooling=true;Trust Server Certificate=true";
    targetDescription = $"{pgHost}:{pgPort}/{pgDatabase}";
}
else
{
    var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
    targetDescription = $"{builder.Host}:{builder.Port}/{builder.Database}";
}

Console.WriteLine(commit
    ? $"COMMIT mode — writing to {targetDescription}"
    : $"DRY RUN — validating against {targetDescription}, no writes (pass --commit to apply)");

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddDataProtection();
services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connectionString));
services.AddIdentityCore<User>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

// Sanity check the connection early with a clear error instead of a deep EF stack trace.
try
{
    await context.Database.CanConnectAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Could not connect to the database: {ex.Message}");
    return 1;
}

if (!await roleManager.RoleExistsAsync(AppRoles.User))
{
    Console.WriteLine($"Role '{AppRoles.User}' does not exist on this database — expected it to " +
                       "already be seeded by the running app. Aborting rather than guessing.");
    return 1;
}

var questionIds = await context.Questions
    .Where(q => q.IsSkill && q.Active && q.SkillType != null)
    .ToDictionaryAsync(q => (int)q.SkillType!, q => q.QuestionId);

if (questionIds.Count != 4)
{
    Console.WriteLine($"Expected 4 active skill questions, one per goal area, found {questionIds.Count}. " +
                       "Aborting — Skill rows would have nowhere valid to point.");
    return 1;
}

var currentGenerationId = await context.ScholarGenerations
    .Where(g => g.IsCurrent)
    .Select(g => (int?)g.Id)
    .FirstOrDefaultAsync();

Console.WriteLine(currentGenerationId.HasValue
    ? $"Tagging new accounts with the current generation (id={currentGenerationId})."
    : "No generation is marked current — new accounts will have no generation set.");

var existingEmails = new HashSet<string>(
    await context.Users.Where(u => u.Email != null).Select(u => u.Email!.ToLower()).ToListAsync(),
    StringComparer.Ordinal);

using var reader = new StreamReader(csvPath, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
{
    HeaderValidated = null,
    MissingFieldFound = null,
    BadDataFound = null,
    TrimOptions = TrimOptions.Trim,
    DetectDelimiter = true
});

await csv.ReadAsync();
csv.ReadHeader();
var headers = csv.HeaderRecord ?? Array.Empty<string>();

int FindColumn(Func<string, bool> predicate, string label)
{
    var idx = Array.FindIndex(headers, h => predicate(h ?? string.Empty));
    if (idx < 0) throw new InvalidOperationException($"Could not find a column for: {label}");
    return idx;
}

bool Has(string header, params string[] all) =>
    all.All(k => header.Contains(k, StringComparison.OrdinalIgnoreCase));

int emailCol, nameCol, statusCol;
int softCol, softRatingCol, hardCol, hardRatingCol;
int interCol, interRatingCol, knowCol, knowRatingCol;

try
{
    emailCol = FindColumn(h => Has(h, "email"), "email");
    nameCol = FindColumn(h => Has(h, "name") && !Has(h, "status"), "full name");
    statusCol = FindColumn(h => Has(h, "scholar status"), "scholar status");
    softCol = FindColumn(h => Has(h, "please list", "soft skill"), "soft skill");
    softRatingCol = FindColumn(h => Has(h, "please rate", "personal development"), "soft skill rating");
    hardCol = FindColumn(h => Has(h, "please list", "hard skill"), "hard skill");
    hardRatingCol = FindColumn(h => Has(h, "please rate", "professional development"), "hard skill rating");
    interCol = FindColumn(h => Has(h, "please list", "interpersonal skill"), "interpersonal skill");
    interRatingCol = FindColumn(h => Has(h, "please rate", "teamwork"), "interpersonal skill rating");
    knowCol = FindColumn(h => Has(h, "please list", "knowledge"), "knowledge skill");
    knowRatingCol = FindColumn(h => Has(h, "please rate", "knowledge"), "knowledge skill rating");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Header mismatch: {ex.Message}");
    Console.WriteLine("Headers found: " + string.Join(" | ", headers));
    return 1;
}

var slotColumns = new (int Status, int TextCol, int RatingCol)[]
{
    (1, softCol, softRatingCol),
    (2, hardCol, hardRatingCol),
    (3, interCol, interRatingCol),
    (4, knowCol, knowRatingCol),
};

static (string First, string Last) SplitName(string fullName)
{
    var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return parts.Length switch
    {
        0 => ("", ""),
        1 => (parts[0], ""),
        _ => (parts[0], string.Join(' ', parts.Skip(1)))
    };
}

static bool LooksLikeEmail(string email)
{
    var at = email.IndexOf('@');
    return at > 0 && at < email.Length - 1 && email.IndexOf('@', at + 1) < 0 &&
           email.LastIndexOf('.') > at && !email.Contains(' ');
}

static ScholarStatus MapStatus(string raw)
{
    var s = raw.Trim();
    if (s.Contains("senior", StringComparison.OrdinalIgnoreCase)) return ScholarStatus.Senior;
    if (s.Contains("junior", StringComparison.OrdinalIgnoreCase)) return ScholarStatus.Junior;
    return ScholarStatus.Unassigned;
}

static string Describe(ScholarStatus status) => status switch
{
    ScholarStatus.Junior => "Junior",
    ScholarStatus.Senior => "Senior",
    ScholarStatus.Alumni => "Alumni",
    ScholarStatus.Withdrawn => "Withdrawn",
    _ => "Unassigned"
};

static string GeneratePassword()
{
    const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    const string lower = "abcdefghijkmnpqrstuvwxyz";
    const string digits = "23456789";
    const string symbols = "!@#$%*?";
    var all = upper + lower + digits + symbols;

    var chars = new List<char> { Pick(upper), Pick(lower), Pick(digits), Pick(symbols) };
    while (chars.Count < 12) chars.Add(Pick(all));

    for (var i = chars.Count - 1; i > 0; i--)
    {
        var j = RandomNumberGenerator.GetInt32(i + 1);
        (chars[i], chars[j]) = (chars[j], chars[i]);
    }

    return new string(chars.ToArray());

    static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}

var created = 0;
var skippedExisting = 0;
var skippedEmptyRow = 0;
var failed = 0;
var unassignedStatus = 0;
var seenInFile = new HashSet<string>(StringComparer.Ordinal);
var credentials = new List<(string First, string Last, string Email, string Password)>();
var issues = new List<string>();

var rowNumber = 1; // header was row 1
while (await csv.ReadAsync())
{
    rowNumber++;

    var email = csv.GetField(emailCol)?.Trim();
    if (string.IsNullOrWhiteSpace(email))
    {
        skippedEmptyRow++;
        continue;
    }

    var normalized = email.ToLowerInvariant();
    var fullName = csv.GetField(nameCol)?.Trim() ?? "";
    var (first, last) = SplitName(fullName);
    var statusRaw = csv.GetField(statusCol)?.Trim() ?? "";
    var status = MapStatus(statusRaw);

    if (!LooksLikeEmail(normalized))
    {
        failed++;
        issues.Add($"Row {rowNumber}: '{email}' does not look like a valid email — skipped.");
        continue;
    }

    if (string.IsNullOrWhiteSpace(first))
    {
        failed++;
        issues.Add($"Row {rowNumber} ({email}): no name — skipped.");
        continue;
    }

    if (status == ScholarStatus.Unassigned)
    {
        unassignedStatus++;
        issues.Add($"Row {rowNumber} ({email}): status '{statusRaw}' did not match Junior/Senior — " +
                    "left as Unassigned.");
    }

    if (!seenInFile.Add(normalized))
    {
        issues.Add($"Row {rowNumber} ({email}): duplicate email within the file — skipped.");
        continue;
    }

    if (existingEmails.Contains(normalized))
    {
        skippedExisting++;
        issues.Add($"Row {rowNumber} ({email}): account already exists — left untouched, skipped.");
        continue;
    }

    var skills = slotColumns
        .Select(s => (s.Status, Text: csv.GetField(s.TextCol)?.Trim(), Rating: csv.GetField(s.RatingCol)?.Trim()))
        .Where(s => !string.IsNullOrWhiteSpace(s.Text))
        .ToList();

    if (!commit)
    {
        created++;
        continue;
    }

    var password = GeneratePassword();
    var user = new User
    {
        UserName = email,
        Email = email,
        FirstName = first,
        LastName = last,
        Title = Describe(status),
        ScholarStatus = status,
        GenerationId = currentGenerationId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        IsActive = true,
        EmailConfirmed = false,
        MustChangePassword = true
    };

    var result = await userManager.CreateAsync(user, password);
    if (!result.Succeeded)
    {
        failed++;
        issues.Add($"Row {rowNumber} ({email}): failed to create — " +
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        continue;
    }

    await userManager.AddToRoleAsync(user, AppRoles.User);

    foreach (var skill in skills)
    {
        context.Skills.Add(new Skill
        {
            ScholarId = user.Id,
            QuestionId = questionIds[skill.Status],
            SkillAnswer = skill.Text!,
            Slot = skill.Status,
            Active = true,
            SatisfactionRating = string.IsNullOrWhiteSpace(skill.Rating) ? null : skill.Rating
        });
    }

    await context.SaveChangesAsync();

    existingEmails.Add(normalized);
    created++;
    credentials.Add((first, last, email, password));
}

var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

if (issues.Count > 0)
{
    var issuesPath = Path.Combine(outDir, $"scholar-import-issues-{timestamp}.txt");
    await File.WriteAllLinesAsync(issuesPath, issues);
    Console.WriteLine($"Issues log: {issuesPath}");
}

if (commit && credentials.Count > 0)
{
    var credPath = Path.Combine(outDir, $"scholar-import-credentials-{timestamp}.csv");
    var lines = new List<string> { "First name,Last name,Email,Temporary password" };
    lines.AddRange(credentials.Select(c => $"{c.First},{c.Last},{c.Email},{c.Password}"));
    await File.WriteAllLinesAsync(credPath, lines);
    Console.WriteLine($"Credentials (share securely, not recoverable afterwards): {credPath}");
}

Console.WriteLine();
Console.WriteLine($"{(commit ? "Created" : "Would create")}: {created}");
Console.WriteLine($"Skipped (already existed): {skippedExisting}");
Console.WriteLine($"Skipped (blank row): {skippedEmptyRow}");
Console.WriteLine($"Unrecognized status (left Unassigned): {unassignedStatus}");
Console.WriteLine($"Failed: {failed}");

return 0;
