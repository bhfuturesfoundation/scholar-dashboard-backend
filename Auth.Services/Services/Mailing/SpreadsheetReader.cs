using ClosedXML.Excel;
using System.Text;

namespace Auth.Services.Services.Mailing
{
    /// <summary>
    /// Reads CSV and Excel uploads into headers plus rows, and matches loosely-named columns.
    ///
    /// Shared by the firm directory and scholar intake because both take spreadsheets from
    /// people rather than systems: headers arrive as "Email", "E-mail", "Mail" or
    /// "Kontakt email" depending on who made the file. Forcing an exact header would mean
    /// every import starts with someone editing the file by hand.
    /// </summary>
    public static class SpreadsheetReader
    {
        public static (List<string> Headers, List<string?[]> Rows) Read(Stream stream, string fileName)
        {
            var isExcel = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase);

            return isExcel ? ReadExcel(stream) : ReadCsv(stream);
        }

        /// <summary>
        /// Maps logical field names to column indexes using the supplied alias table.
        /// Aliases are compared folded, so casing, punctuation and diacritics don't matter.
        /// </summary>
        public static Dictionary<string, int> BuildColumnMap(
            List<string> headers, Dictionary<string, string[]> aliases)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < headers.Count; i++)
            {
                var folded = TextNormalizer.FoldToWords(headers[i]);
                if (folded.Length == 0) continue;

                foreach (var (field, candidates) in aliases)
                {
                    if (map.ContainsKey(field)) continue;
                    if (!candidates.Contains(folded, StringComparer.Ordinal)) continue;

                    map[field] = i;
                    break;
                }
            }

            return map;
        }

        public static string? Value(string?[] row, Dictionary<string, int> map, string field) =>
            map.TryGetValue(field, out var index) && index < row.Length ? row[index] : null;

        // ── Readers ───────────────────────────────────────────────────────────

        private static (List<string>, List<string?[]>) ReadExcel(Stream stream)
        {
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.First();

            var headers = new List<string>();
            var rows = new List<string?[]>();

            var range = sheet.RangeUsed();
            if (range is null) return (headers, rows);

            foreach (var cell in range.FirstRow().Cells())
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

        private static (List<string>, List<string?[]>) ReadCsv(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var records = ParseCsv(reader.ReadToEnd());

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

        /// <summary>
        /// Minimal RFC 4180 reader: quoted fields, embedded separators, doubled quotes and
        /// newlines inside quotes. Hand-written rather than via CsvHelper because the headers
        /// are unknown and the rows are ragged, which the mapping-based API fights.
        /// </summary>
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
    }
}
