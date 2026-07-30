using Auth.Services.Services.Operations;

namespace Auth.Services.Interfaces.Operations
{
    public enum ScholarInclusion
    {
        /// <summary>The current cohort. The default — deactivated accounts are opt-in.</summary>
        ActiveOnly = 0,

        /// <summary>Deactivated accounts only, for reconciliation.</summary>
        InactiveOnly = 1,

        All = 2
    }

    public class ScholarExportFilter
    {
        public ScholarInclusion Include { get; set; } = ScholarInclusion.ActiveOnly;
    }

    /// <summary>
    /// Builds the scholar roster export. Available to program managers as well as admins:
    /// it contains only fields those roles already see in the UI.
    /// </summary>
    public interface IScholarExportService
    {
        Task<ExportTable> BuildAsync(ScholarExportFilter filter, CancellationToken cancellationToken = default);
    }
}
