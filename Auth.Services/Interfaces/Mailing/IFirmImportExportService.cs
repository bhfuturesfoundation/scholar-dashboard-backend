using Auth.Models.DTOs.Mailing;
using Auth.Models.Enums.Mailing;
using Auth.Services.Services.Operations;

namespace Auth.Services.Interfaces.Mailing
{
    public class FirmImportOptions
    {
        /// <summary>
        /// Validate and report without writing anything. The import screen always runs this
        /// first — a spreadsheet from a third party is the most likely way this directory
        /// gets corrupted, and "500 rows failed" is much cheaper to discover before the write.
        /// </summary>
        public bool DryRun { get; set; } = true;

        /// <summary>Update firms that already exist (matched on email) rather than skipping them.</summary>
        public bool UpdateExisting { get; set; } = true;

        /// <summary>Assign a firm type from keywords when the file doesn't supply one.</summary>
        public bool AutoCategorize { get; set; } = true;

        /// <summary>Derive contact names from email addresses during import.</summary>
        public bool DetectContactNames { get; set; } = true;

        /// <summary>Applied to rows whose file provides no type.</summary>
        public int? DefaultFirmTypeId { get; set; }
    }

    public class FirmExportFilter
    {
        public int? FirmTypeId { get; set; }
        public int? FirmGroupId { get; set; }
        public FirmStatus? Status { get; set; }
        public bool IncludeNotes { get; set; } = true;
    }

    /// <summary>
    /// Imports and exports the firm directory as CSV or Excel.
    ///
    /// Import is deliberately forgiving about column naming — partnerships spreadsheets come
    /// from many sources and no two use the same headers — and deliberately strict about
    /// what it will write.
    /// </summary>
    public interface IFirmImportExportService
    {
        Task<FirmImportResultDto> ImportAsync(
            Stream fileStream,
            string fileName,
            FirmImportOptions options,
            string? userId,
            string userName,
            CancellationToken cancellationToken = default);

        Task<ExportTable> BuildExportAsync(
            FirmExportFilter filter, CancellationToken cancellationToken = default);

        /// <summary>
        /// A template file with the expected headers and one example row, so nobody has to
        /// guess the format before their first import.
        /// </summary>
        ExportTable BuildImportTemplate();
    }
}
