using ClosedXML.Excel;
using System.Globalization;
using System.Text;

namespace Auth.Services.Services.Operations
{
    /// <summary>A sheet of data ready to be written as CSV or Excel.</summary>
    public class ExportTable
    {
        public string Name { get; set; } = "Sheet1";
        public List<string> Headers { get; set; } = new();
        public List<List<object?>> Rows { get; set; } = new();
    }

    /// <summary>
    /// Writes tabular data as CSV or Excel.
    ///
    /// Shared by scholar exports, firm exports and campaign delivery logs, so the awkward
    /// parts are solved once: Excel's habit of mangling values that look like formulas or
    /// dates, and CSV's quoting rules.
    /// </summary>
    public static class TabularExporter
    {
        public const string CsvContentType = "text/csv";
        public const string ExcelContentType =
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        public static byte[] ToCsv(ExportTable table)
        {
            var sb = new StringBuilder();

            // UTF-8 BOM: without it Excel opens the file as ANSI and every Bosnian
            // diacritic in a firm or scholar name renders as mojibake.
            sb.Append('﻿');

            sb.AppendLine(string.Join(",", table.Headers.Select(h => CsvEscape(h))));

            foreach (var row in table.Rows)
                sb.AppendLine(string.Join(",", row.Select(v => CsvEscape(Format(v)))));

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public static byte[] ToExcel(params ExportTable[] tables)
        {
            using var workbook = new XLWorkbook();

            foreach (var table in tables)
            {
                // Excel sheet names cap at 31 chars and reject : \ / ? * [ ]
                var sheetName = SanitizeSheetName(table.Name);
                var sheet = workbook.Worksheets.Add(sheetName);

                for (var c = 0; c < table.Headers.Count; c++)
                {
                    var cell = sheet.Cell(1, c + 1);
                    cell.Value = table.Headers[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#0b1b3d");
                    cell.Style.Font.FontColor = XLColor.White;
                }

                for (var r = 0; r < table.Rows.Count; r++)
                {
                    var row = table.Rows[r];

                    for (var c = 0; c < row.Count && c < table.Headers.Count; c++)
                        WriteCell(sheet.Cell(r + 2, c + 1), row[c]);
                }

                if (table.Headers.Count > 0)
                {
                    sheet.SheetView.FreezeRows(1);
                    sheet.RangeUsed()?.SetAutoFilter();
                    sheet.Columns().AdjustToContents(1, 200, 8, 60);
                }
            }

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        /// <summary>
        /// Writes a value with its real type where that's useful, and as text where Excel's
        /// coercion would corrupt it.
        /// </summary>
        private static void WriteCell(IXLCell cell, object? value)
        {
            switch (value)
            {
                case null:
                    return;

                case bool b:
                    cell.Value = b;
                    return;

                case DateTime dt:
                    cell.Value = dt;
                    cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                    return;

                case int or long or short or double or decimal or float:
                    cell.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return;

                default:
                    var text = Format(value);

                    // A leading =, +, - or @ makes Excel treat the cell as a formula. That is
                    // both a corruption risk for names starting with "-" and the CSV-injection
                    // vector: a crafted value in an imported firm name could execute on the
                    // machine of whoever opens the export.
                    if (text.Length > 0 && (text[0] is '=' or '+' or '-' or '@'))
                    {
                        cell.SetValue(text);
                        cell.Style.NumberFormat.Format = "@";
                        return;
                    }

                    cell.Value = text;
                    return;
            }
        }

        private static string Format(object? value) => value switch
        {
            null => string.Empty,
            bool b => b ? "Yes" : "No",
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            // Same formula-injection guard as the Excel path: a CSV opened in Excel is just
            // as capable of executing a leading =.
            if (value[0] is '=' or '+' or '-' or '@')
                value = "'" + value;

            var needsQuoting = value.Contains(',') || value.Contains('"')
                || value.Contains('\n') || value.Contains('\r');

            return needsQuoting ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        }

        private static string SanitizeSheetName(string name)
        {
            var cleaned = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
            if (string.IsNullOrWhiteSpace(cleaned)) cleaned = "Sheet";
            return cleaned.Length <= 31 ? cleaned : cleaned[..31];
        }
    }
}
