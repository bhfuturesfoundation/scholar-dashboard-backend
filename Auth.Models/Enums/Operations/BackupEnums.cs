namespace Auth.Models.Enums.Operations
{
    /// <summary>
    /// Available backup formats.
    ///
    /// Note there is no BACPAC. That is a SQL Server / Azure SQL format and this application
    /// runs on PostgreSQL, where it simply does not exist. <see cref="PgDump"/> is the
    /// equivalent — it is what you restore with, and it is the only format here that captures
    /// schema, indexes and constraints rather than just rows.
    /// </summary>
    public enum BackupFormat
    {
        /// <summary>
        /// Every table as JSON in one file. Portable and diff-able, needs no external tooling,
        /// and is readable by anything. Data only — no schema.
        /// </summary>
        Json = 0,

        /// <summary>
        /// One CSV per table inside a zip. The format to hand someone who wants to open it in
        /// Excel. Data only.
        /// </summary>
        CsvArchive = 1,

        /// <summary>
        /// SQL INSERT statements. Restorable into an existing schema with psql, without any
        /// .NET tooling. Data only.
        /// </summary>
        SqlScript = 2,

        /// <summary>
        /// Native pg_dump custom-format archive. The real answer for disaster recovery:
        /// schema plus data, restorable with pg_restore. Requires the pg_dump binary to be
        /// present and at least the server's major version — the service reports clearly
        /// when it isn't rather than producing a silently truncated file.
        /// </summary>
        PgDump = 3
    }

    public enum BackupStatus
    {
        Running = 0,
        Completed = 1,
        Failed = 2
    }
}
