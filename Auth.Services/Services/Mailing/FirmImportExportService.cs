using Auth.Models.Data;
using Auth.Models.DTOs.Mailing;
using Auth.Models.Entities.Mailing;
using Auth.Models.Enums.Mailing;
using Auth.Services.Interfaces.Mailing;
using Auth.Services.Services.Operations;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace Auth.Services.Services.Mailing
{
    /// <summary>
    /// Reads firm spreadsheets (CSV or Excel) into the directory, and writes them back out.
    ///
    /// Column matching is fuzzy on purpose: partnership lists arrive from conferences,
    /// chambers of commerce and colleagues' personal spreadsheets, and none of them agree on
    /// whether the column is "Email", "E-mail", "Mail" or "Kontakt email". Forcing an exact
    /// header would mean every import starts with someone editing the file by hand.
    /// </summary>
    public class FirmImportExportService : IFirmImportExportService
    {
        /// <summary>
        /// Accepted header spellings per field, folded. English and Bosnian/Croatian/Serbian,
        /// since both appear in real source files.
        /// </summary>
        private static readonly Dictionary<string, string[]> ColumnAliases = new(StringComparer.Ordinal)
        {
            ["name"] = new[] { "name", "firm", "firm name", "company", "company name", "organisation", "organization", "naziv", "firma", "kompanija", "preduzece", "ime" },
            ["legalname"] = new[] { "legal name", "registered name", "puni naziv", "pravni naziv" },
            ["email"] = new[] { "email", "e mail", "mail", "email address", "e mail address", "kontakt email", "eposta", "e posta" },
            ["website"] = new[] { "website", "web", "url", "site", "web site", "web stranica", "stranica" },
            ["phone"] = new[] { "phone", "telephone", "tel", "mobile", "phone number", "telefon", "broj telefona", "kontakt telefon" },
            ["address"] = new[] { "address", "street", "adresa", "ulica" },
            ["city"] = new[] { "city", "town", "grad", "mjesto", "mesto" },
            ["country"] = new[] { "country", "drzava", "zemlja" },
            ["type"] = new[] { "type", "firm type", "category", "sector", "industry", "tip", "vrsta", "kategorija", "djelatnost", "delatnost" },
            ["contactname"] = new[] { "contact", "contact name", "contact person", "person", "kontakt osoba", "osoba", "kontakt ime" },
            ["contactrole"] = new[] { "role", "title", "position", "job title", "pozicija", "funkcija", "radno mjesto" },
            ["notes"] = new[] { "notes", "note", "comment", "comments", "napomena", "napomene", "komentar" },
        };

        /// <summary>Cap on reported issues so a badly broken file can't return megabytes.</summary>
        private const int MaxReportedIssues = 200;

        private readonly ApplicationDbContext _context;
        private readonly IContactNameExtractor _nameExtractor;
        private readonly IFirmCategorizer _categorizer;
        private readonly ILogger<FirmImportExportService> _logger;

        public FirmImportExportService(
            ApplicationDbContext context,
            IContactNameExtractor nameExtractor,
            IFirmCategorizer categorizer,
            ILogger<FirmImportExportService> logger)
        {
            _context = context;
            _nameExtractor = nameExtractor;
            _categorizer = categorizer;
            _logger = logger;
        }

        public async Task<FirmImportResultDto> ImportAsync(
            Stream fileStream,
            string fileName,
            FirmImportOptions options,
            string? userId,
            string userName,
            CancellationToken cancellationToken = default)
        {
            var isExcel = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase);

            var (headers, rows) = isExcel ? ReadExcel(fileStream) : ReadCsv(fileStream);

            var result = new FirmImportResultDto
            {
                FileName = fileName,
                WasDryRun = options.DryRun,
                TotalRows = rows.Count,
                DetectedColumns = headers
            };

            var map = BuildColumnMap(headers);

            if (!map.ContainsKey("name") && !map.ContainsKey("email"))
            {
                result.FailedCount = rows.Count;
                result.Issues.Add(new FirmImportRowIssueDto
                {
                    RowNumber = 0,
                    Outcome = "Failed",
                    Message = "The file needs at least a firm-name or an email column. " +
                              $"Found: {string.Join(", ", headers)}."
                });
                return result;
            }

            var types = await _context.FirmTypes.AsNoTracking().ToListAsync(cancellationToken);

            var typeBySlug = types.ToDictionary(t => t.Slug, StringComparer.OrdinalIgnoreCase);
            var typeByName = types
                .GroupBy(t => TextNormalizer.FoldToWords(t.Name))
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            // One lookup of every existing email up front. Checking per row would be an extra
            // round trip per line — noticeable at a few thousand rows.
            var existingByEmail = await _context.Firms
                .Where(f => f.NormalizedEmail != null)
                .ToDictionaryAsync(f => f.NormalizedEmail!, cancellationToken);

            var batch = new FirmImportBatch
            {
                FileName = fileName,
                Format = isExcel ? ImportFormat.Excel : ImportFormat.Csv,
                TotalRows = rows.Count,
                WasDryRun = options.DryRun,
                CreatedByUserId = userId ?? string.Empty,
                CreatedByName = userName
            };

            if (!options.DryRun)
            {
                _context.FirmImportBatches.Add(batch);
                await _context.SaveChangesAsync(cancellationToken);
            }

            // Guards against a file containing the same address twice — the second occurrence
            // would otherwise violate the unique index at SaveChanges and fail the whole import.
            var seenInFile = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowNumber = i + 2; // +1 for zero-index, +1 for the header line

                var name = Value(row, map, "name");
                var email = Value(row, map, "email");
                var normalizedEmail = TextNormalizer.NormalizeEmail(email);

                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(normalizedEmail))
                {
                    // Trailing blank lines are normal in exported spreadsheets, not an error
                    // worth reporting.
                    result.SkippedCount++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(normalizedEmail) && !LooksLikeEmail(normalizedEmail))
                {
                    result.FailedCount++;
                    AddIssue(result, rowNumber, name, email, "Failed", "Email address is not valid.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    // A row with only an address is still worth keeping — the domain is often
                    // enough to identify the firm later.
                    name = TextNormalizer.CapitalizeName(TextNormalizer.DomainLabel(email));
                    if (string.IsNullOrWhiteSpace(name)) name = email!;
                }

                if (normalizedEmail is not null && !seenInFile.Add(normalizedEmail))
                {
                    result.SkippedCount++;
                    AddIssue(result, rowNumber, name, email, "Skipped", "Duplicate address within this file.");
                    continue;
                }

                Firm? existing = null;
                if (normalizedEmail is not null)
                    existingByEmail.TryGetValue(normalizedEmail, out existing);

                if (existing is not null && !options.UpdateExisting)
                {
                    result.SkippedCount++;
                    AddIssue(result, rowNumber, name, email, "Skipped", "Firm already exists.");
                    continue;
                }

                var firm = existing ?? new Firm
                {
                    CreatedByUserId = userId,
                    ImportBatchId = options.DryRun ? null : batch.Id,
                    CreatedAt = DateTime.UtcNow
                };

                firm.Name = name.Trim();
                firm.LegalName = Value(row, map, "legalname")?.Trim() ?? firm.LegalName;
                firm.Email = email?.Trim() ?? firm.Email;
                firm.NormalizedEmail = normalizedEmail ?? firm.NormalizedEmail;
                firm.Website = Value(row, map, "website")?.Trim() ?? firm.Website;
                firm.Phone = Value(row, map, "phone")?.Trim() ?? firm.Phone;
                firm.Address = Value(row, map, "address")?.Trim() ?? firm.Address;
                firm.City = Value(row, map, "city")?.Trim() ?? firm.City;
                firm.Country = Value(row, map, "country")?.Trim() ?? firm.Country;
                firm.ContactPersonRole = Value(row, map, "contactrole")?.Trim() ?? firm.ContactPersonRole;

                var notes = Value(row, map, "notes");
                if (!string.IsNullOrWhiteSpace(notes)) firm.Notes = notes.Trim();

                // A firm with no usable address can't be mailed. Flagging it as Incomplete
                // keeps it visible for someone to fix rather than silently in the send list.
                if (string.IsNullOrWhiteSpace(firm.NormalizedEmail))
                    firm.Status = FirmStatus.Incomplete;

                // ── Firm type ─────────────────────────────────────────────────
                var typeText = Value(row, map, "type");
                FirmType? resolvedType = null;

                if (!string.IsNullOrWhiteSpace(typeText))
                {
                    typeBySlug.TryGetValue(TextNormalizer.Slugify(typeText), out resolvedType);
                    if (resolvedType is null)
                        typeByName.TryGetValue(TextNormalizer.FoldToWords(typeText), out resolvedType);
                }

                if (resolvedType is not null)
                {
                    firm.FirmTypeId = resolvedType.Id;
                }
                else if (firm.FirmTypeId is null)
                {
                    if (options.AutoCategorize)
                    {
                        var suggestion = _categorizer.Suggest(firm.Name, firm.Website, firm.Email, types);
                        if (suggestion.HasSuggestion && suggestion.IsConfident)
                        {
                            firm.FirmTypeId = suggestion.FirmTypeId;
                            result.AutoCategorizedCount++;
                        }
                    }

                    firm.FirmTypeId ??= options.DefaultFirmTypeId;
                }

                // ── Contact name ──────────────────────────────────────────────
                var providedContact = Value(row, map, "contactname");

                if (!string.IsNullOrWhiteSpace(providedContact))
                {
                    // A name in the file was chosen by a human, so it outranks detection.
                    firm.ContactPersonName = providedContact.Trim();
                    firm.ContactNameSource = ContactNameSource.Imported;
                    firm.ContactNameConfidence = NameConfidence.High;
                    result.NamesDetectedCount++;
                }
                else if (options.DetectContactNames &&
                         firm.ContactNameSource != ContactNameSource.Manual &&
                         string.IsNullOrWhiteSpace(firm.ContactPersonName))
                {
                    var extracted = _nameExtractor.Extract(firm.Email, firm.Name);
                    if (extracted.IsUsable)
                    {
                        firm.ContactPersonName = extracted.FullName;
                        firm.ContactNameSource = extracted.Source;
                        firm.ContactNameConfidence = extracted.Confidence;
                        result.NamesDetectedCount++;
                    }
                }

                firm.UpdatedAt = DateTime.UtcNow;

                if (existing is null)
                {
                    result.CreatedCount++;
                    if (!options.DryRun)
                    {
                        _context.Firms.Add(firm);
                        if (normalizedEmail is not null) existingByEmail[normalizedEmail] = firm;
                    }
                }
                else
                {
                    result.UpdatedCount++;
                }
            }

            if (!options.DryRun)
            {
                batch.CreatedCount = result.CreatedCount;
                batch.UpdatedCount = result.UpdatedCount;
                batch.SkippedCount = result.SkippedCount;
                batch.FailedCount = result.FailedCount;
                batch.ErrorReport = result.Issues.Count == 0
                    ? null
                    : string.Join("\n", result.Issues.Take(50).Select(i => $"Row {i.RowNumber}: {i.Message}"));

                await _context.SaveChangesAsync(cancellationToken);
                result.BatchId = batch.Id;
            }

            _logger.LogInformation(
                "Firm import ({Mode}) of {File}: {Created} created, {Updated} updated, {Skipped} skipped, {Failed} failed.",
                options.DryRun ? "dry run" : "committed", fileName,
                result.CreatedCount, result.UpdatedCount, result.SkippedCount, result.FailedCount);

            return result;
        }

        public async Task<ExportTable> BuildExportAsync(
            FirmExportFilter filter, CancellationToken cancellationToken = default)
        {
            var query = _context.Firms
                .AsNoTracking()
                .Include(f => f.FirmType)!
                    .ThenInclude(t => t!.FirmGroup)
                .AsQueryable();

            if (filter.FirmTypeId.HasValue)
                query = query.Where(f => f.FirmTypeId == filter.FirmTypeId);

            if (filter.FirmGroupId.HasValue)
                query = query.Where(f => f.FirmType != null && f.FirmType.FirmGroupId == filter.FirmGroupId);

            if (filter.Status.HasValue)
                query = query.Where(f => f.Status == filter.Status);

            var firms = await query.OrderBy(f => f.Name).ToListAsync(cancellationToken);

            var table = new ExportTable
            {
                Name = "Firms",
                Headers = new List<string>
                {
                    "Name", "Legal name", "Type", "Group", "Email", "Contact person",
                    "Contact role", "Name source", "Name confidence", "Website", "Phone",
                    "City", "Country", "Status", "Last contacted", "Times contacted", "Added"
                }
            };

            if (filter.IncludeNotes) table.Headers.Add("Notes");

            foreach (var firm in firms)
            {
                var row = new List<object?>
                {
                    firm.Name,
                    firm.LegalName,
                    firm.FirmType?.Name,
                    firm.FirmType?.FirmGroup?.Name,
                    firm.Email,
                    firm.ContactPersonName,
                    firm.ContactPersonRole,
                    firm.ContactNameSource.ToString(),
                    firm.ContactNameConfidence.ToString(),
                    firm.Website,
                    firm.Phone,
                    firm.City,
                    firm.Country,
                    firm.Status.ToString(),
                    firm.LastContactedAt,
                    firm.ContactCount,
                    firm.CreatedAt
                };

                if (filter.IncludeNotes) row.Add(firm.Notes);

                table.Rows.Add(row);
            }

            return table;
        }

        public ExportTable BuildImportTemplate() => new()
        {
            Name = "Firms",
            Headers = new List<string>
            {
                "Name", "Legal name", "Type", "Email", "Contact person", "Contact role",
                "Website", "Phone", "City", "Country", "Notes"
            },
            Rows = new List<List<object?>>
            {
                new()
                {
                    "Acme d.o.o.", "Acme društvo s ograničenom odgovornošću", "IT Company",
                    "amir.hodzic@acme.ba", "", "Direktor",
                    "https://acme.ba", "+387 33 123 456", "Sarajevo", "Bosnia and Herzegovina",
                    "Met at the 2026 tech conference"
                },
                new()
                {
                    "Banka Primjer", "", "Bank",
                    "kontakt@banka-primjer.ba", "", "",
                    "https://banka-primjer.ba", "", "Mostar", "Bosnia and Herzegovina",
                    "Leave Contact person blank to have it detected from the address"
                }
            }
        };

        // ── File readers ──────────────────────────────────────────────────────

        private static (List<string> Headers, List<string?[]> Rows) ReadExcel(Stream stream)
        {
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.First();

            var headers = new List<string>();
            var rows = new List<string?[]>();

            var range = sheet.RangeUsed();
            if (range is null) return (headers, rows);

            var firstRow = range.FirstRow();
            foreach (var cell in firstRow.Cells())
                headers.Add(cell.GetString().Trim());

            foreach (var row in range.RowsUsed().Skip(1))
            {
                var values = new string?[headers.Count];

                for (var c = 0; c < headers.Count; c++)
                {
                    var text = row.Cell(c + 1).GetString();
                    values[c] = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                }

                if (values.All(string.IsNullOrWhiteSpace)) continue;
                rows.Add(values);
            }

            return (headers, rows);
        }

        /// <summary>
        /// Minimal RFC 4180 reader: quoted fields, embedded commas, doubled quotes and
        /// newlines inside quotes. Written by hand rather than pulled from CsvHelper because
        /// the input has unknown headers and ragged rows, which the mapping-based API fights.
        /// </summary>
        private static (List<string> Headers, List<string?[]> Rows) ReadCsv(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = reader.ReadToEnd();

            var records = ParseCsv(content);

            if (records.Count == 0) return (new List<string>(), new List<string?[]>());

            var headers = records[0].Select(h => h?.Trim() ?? string.Empty).ToList();

            var rows = records
                .Skip(1)
                .Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v)))
                .Select(r =>
                {
                    // Rows can be shorter or longer than the header; normalise to header width
                    // so indexing by column never goes out of range.
                    var normalised = new string?[headers.Count];
                    for (var i = 0; i < headers.Count && i < r.Length; i++)
                        normalised[i] = string.IsNullOrWhiteSpace(r[i]) ? null : r[i]!.Trim();
                    return normalised;
                })
                .ToList();

            return (headers, rows);
        }

        private static List<string?[]> ParseCsv(string content)
        {
            var records = new List<string?[]>();
            var fields = new List<string?>();
            var field = new StringBuilder();

            var inQuotes = false;
            var i = 0;

            while (i < content.Length)
            {
                var ch = content[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        // "" inside a quoted field is a literal quote.
                        if (i + 1 < content.Length && content[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                            continue;
                        }

                        inQuotes = false;
                        i++;
                        continue;
                    }

                    field.Append(ch);
                    i++;
                    continue;
                }

                switch (ch)
                {
                    case '"':
                        inQuotes = true;
                        i++;
                        break;

                    case ',':
                    case ';': // Excel on a European locale writes semicolons
                        fields.Add(field.ToString());
                        field.Clear();
                        i++;
                        break;

                    case '\r':
                        i++;
                        break;

                    case '\n':
                        fields.Add(field.ToString());
                        field.Clear();
                        records.Add(fields.ToArray());
                        fields.Clear();
                        i++;
                        break;

                    default:
                        field.Append(ch);
                        i++;
                        break;
                }
            }

            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                records.Add(fields.ToArray());
            }

            return records;
        }

        // ── Column mapping ────────────────────────────────────────────────────

        private static Dictionary<string, int> BuildColumnMap(List<string> headers)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < headers.Count; i++)
            {
                var folded = TextNormalizer.FoldToWords(headers[i]);
                if (folded.Length == 0) continue;

                foreach (var (field, aliases) in ColumnAliases)
                {
                    if (map.ContainsKey(field)) continue;
                    if (!aliases.Contains(folded, StringComparer.Ordinal)) continue;

                    map[field] = i;
                    break;
                }
            }

            return map;
        }

        private static string? Value(string?[] row, Dictionary<string, int> map, string field) =>
            map.TryGetValue(field, out var index) && index < row.Length ? row[index] : null;

        private static bool LooksLikeEmail(string email)
        {
            var at = email.IndexOf('@');
            return at > 0
                && at < email.Length - 1
                && email.IndexOf('@', at + 1) < 0
                && email.LastIndexOf('.') > at
                && !email.Contains(' ');
        }

        private static void AddIssue(
            FirmImportResultDto result, int rowNumber, string? name, string? email, string outcome, string message)
        {
            if (result.Issues.Count >= MaxReportedIssues) return;

            result.Issues.Add(new FirmImportRowIssueDto
            {
                RowNumber = rowNumber,
                FirmName = name,
                Email = email,
                Outcome = outcome,
                Message = message
            });
        }
    }
}
