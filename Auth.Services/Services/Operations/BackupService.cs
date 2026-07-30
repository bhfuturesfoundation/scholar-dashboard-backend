using Auth.Models.Data;
using Auth.Models.Entities.Operations;
using Auth.Models.Enums.Operations;
using Auth.Services.Interfaces.Operations;
using Auth.Services.Interfaces.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Auth.Services.Services.Operations
{
    /// <summary>
    /// Produces database backups in several formats.
    ///
    /// Three of the four formats are implemented in pure C# against the live connection, so
    /// they work on the stock aspnet runtime image with no external binary. The fourth shells
    /// out to pg_dump, which is the only one that captures schema as well as data — it is
    /// what you actually restore from, when it is available.
    ///
    /// Sensitive columns are redacted unless explicitly requested. The default backup is a
    /// complete copy of business data that is NOT a credential store; opting in changes that,
    /// and the choice is recorded on the backup record.
    /// </summary>
    public class BackupService : IBackupService
    {
        /// <summary>
        /// Columns never included unless <c>IncludeSensitiveData</c> is set.
        ///
        /// Password hashes are the obvious one, but refresh tokens matter just as much: they
        /// are live bearer credentials, so a leaked backup containing them is a working set of
        /// sessions rather than merely a historical record. Security and concurrency stamps
        /// are included because they feed Identity's token generation.
        /// </summary>
        private static readonly Dictionary<string, string[]> SensitiveColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AspNetUsers"] = new[]
            {
                "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
                "TwoFactorSecret", "PhoneNumber"
            },
            ["RefreshTokens"] = new[] { "Token", "ReplacedByToken" },
            ["AspNetUserTokens"] = new[] { "Value" },
        };

        private const string RedactedMarker = "[REDACTED]";

        private readonly ApplicationDbContext _context;
        private readonly IDropboxStorage _dropbox;
        private readonly ILogger<BackupService> _logger;

        public BackupService(
            ApplicationDbContext context,
            IDropboxStorage dropbox,
            ILogger<BackupService> logger)
        {
            _context = context;
            _dropbox = dropbox;
            _logger = logger;
        }

        public async Task<BackupArtifact> CreateAsync(
            CreateBackupRequest request,
            string? userId,
            string userName,
            CancellationToken cancellationToken = default)
        {
            var timestamp = DateTime.UtcNow;
            var extension = request.Format switch
            {
                BackupFormat.Json => "json",
                BackupFormat.CsvArchive => "zip",
                BackupFormat.SqlScript => "sql",
                BackupFormat.PgDump => "dump",
                _ => "bin"
            };

            var record = new BackupRecord
            {
                FileName = $"scholar-backup-{timestamp:yyyyMMdd-HHmmss}.{extension}",
                Format = request.Format,
                Status = BackupStatus.Running,
                IncludesSensitiveData = request.IncludeSensitiveData,
                CreatedByUserId = userId,
                CreatedByName = userName,
                IsAutomatic = userId is null,
                StartedAt = timestamp,
                ExpiresAt = request.RetentionDays.HasValue
                    ? timestamp.AddDays(request.RetentionDays.Value)
                    : null
            };

            // Persisted before the work starts, so a crash mid-run leaves evidence rather
            // than nothing. A backup system that fails silently creates false confidence.
            _context.BackupRecords.Add(record);
            await _context.SaveChangesAsync(cancellationToken);

            try
            {
                var (content, contentType, tableCount, rowCount) = request.Format switch
                {
                    BackupFormat.Json => await BuildJsonAsync(request.IncludeSensitiveData, cancellationToken),
                    BackupFormat.CsvArchive => await BuildCsvArchiveAsync(request.IncludeSensitiveData, cancellationToken),
                    BackupFormat.SqlScript => await BuildSqlScriptAsync(request.IncludeSensitiveData, cancellationToken),
                    BackupFormat.PgDump => await BuildPgDumpAsync(cancellationToken),
                    _ => throw new NotSupportedException($"Unsupported backup format: {request.Format}")
                };

                record.SizeBytes = content.Length;
                record.TableCount = tableCount;
                record.RowCount = rowCount;
                record.Status = BackupStatus.Completed;
                record.CompletedAt = DateTime.UtcNow;

                if (request.ArchiveToDropbox && _dropbox.IsConfigured)
                {
                    // Local disk is ephemeral on Railway — a backup that only exists in the
                    // container is gone at the next deploy, which is precisely when it matters.
                    var path = $"/backups/{record.FileName}";
                    var upload = await _dropbox.TryUploadTextAsync(
                        path, Convert.ToBase64String(content), cancellationToken);

                    if (upload.Success)
                    {
                        record.StoragePath = path;
                        record.IsArchived = true;
                    }
                    else
                    {
                        // The download still works, so this is a warning rather than a failure.
                        _logger.LogWarning(
                            "Backup {File} was produced but could not be archived: {Error}",
                            record.FileName, upload.Error);
                    }
                }

                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Backup {File} completed: {Tables} tables, {Rows} rows, {Size} bytes, sensitive={Sensitive}",
                    record.FileName, tableCount, rowCount, content.Length, request.IncludeSensitiveData);

                return new BackupArtifact { Record = record, Content = content, ContentType = contentType };
            }
            catch (Exception ex)
            {
                record.Status = BackupStatus.Failed;
                record.Error = ex.Message;
                record.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogError(ex, "Backup {File} failed.", record.FileName);
                throw;
            }
        }

        public async Task<List<BackupRecord>> GetHistoryAsync(int limit = 50, CancellationToken cancellationToken = default) =>
            await _context.BackupRecords
                .AsNoTracking()
                .OrderByDescending(b => b.StartedAt)
                .Take(Math.Clamp(limit, 1, 500))
                .ToListAsync(cancellationToken);

        public async Task<List<BackupFormatAvailability>> GetFormatAvailabilityAsync(
            CancellationToken cancellationToken = default)
        {
            var pgDumpAvailable = await IsPgDumpAvailableAsync(cancellationToken);

            return new List<BackupFormatAvailability>
            {
                new()
                {
                    Format = BackupFormat.Json,
                    Name = "JSON",
                    Description = "Every table as JSON in one file. Portable, needs no tooling to read.",
                    IsAvailable = true,
                    RestoreInstructions = "Data only, no schema. Restore by importing into a database created from migrations."
                },
                new()
                {
                    Format = BackupFormat.CsvArchive,
                    Name = "CSV archive (.zip)",
                    Description = "One CSV per table, zipped. Open directly in Excel.",
                    IsAvailable = true,
                    RestoreInstructions = "Data only. Intended for inspection and reporting rather than restore."
                },
                new()
                {
                    Format = BackupFormat.SqlScript,
                    Name = "SQL script",
                    Description = "INSERT statements, restorable with psql.",
                    IsAvailable = true,
                    RestoreInstructions = "Create the schema with migrations first, then: psql \"$DATABASE_URL\" -f backup.sql"
                },
                new()
                {
                    Format = BackupFormat.PgDump,
                    Name = "pg_dump archive",
                    Description = "Native PostgreSQL dump — schema and data. The format to restore from.",
                    IsAvailable = pgDumpAvailable,
                    UnavailableReason = pgDumpAvailable
                        ? null
                        : "pg_dump is not installed in this container. Add postgresql-client to the Dockerfile to enable it.",
                    RestoreInstructions = "pg_restore --clean --if-exists -d \"$DATABASE_URL\" backup.dump"
                }
            };
        }

        public async Task<int> PruneExpiredAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            var expired = await _context.BackupRecords
                .Where(b => b.ExpiresAt != null && b.ExpiresAt < now)
                .ToListAsync(cancellationToken);

            if (expired.Count == 0) return 0;

            _context.BackupRecords.RemoveRange(expired);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Pruned {Count} expired backup record(s).", expired.Count);
            return expired.Count;
        }

        // ── Format builders ───────────────────────────────────────────────────

        private async Task<(byte[] Content, string ContentType, int Tables, int Rows)> BuildJsonAsync(
            bool includeSensitive, CancellationToken cancellationToken)
        {
            var tables = await ReadAllTablesAsync(includeSensitive, cancellationToken);

            var payload = new
            {
                generatedAtUtc = DateTime.UtcNow,
                includesSensitiveData = includeSensitive,
                tableCount = tables.Count,
                rowCount = tables.Sum(t => t.Rows.Count),
                tables = tables.ToDictionary(t => t.Name, t => t.Rows)
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

            return (Encoding.UTF8.GetBytes(json), "application/json",
                tables.Count, tables.Sum(t => t.Rows.Count));
        }

        private async Task<(byte[] Content, string ContentType, int Tables, int Rows)> BuildCsvArchiveAsync(
            bool includeSensitive, CancellationToken cancellationToken)
        {
            var tables = await ReadAllTablesAsync(includeSensitive, cancellationToken);

            using var memory = new MemoryStream();

            // Scoped so the archive is fully flushed before ToArray() — reading the stream
            // while the ZipArchive is still open yields a truncated, unopenable file.
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var table in tables)
                {
                    var entry = archive.CreateEntry($"{table.Name}.csv", CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    await using var writer = new StreamWriter(entryStream, Encoding.UTF8);

                    if (table.Columns.Count == 0) continue;

                    await writer.WriteLineAsync(string.Join(",", table.Columns.Select(CsvEscape)));

                    foreach (var row in table.Rows)
                    {
                        var line = string.Join(",", table.Columns.Select(c =>
                            CsvEscape(row.TryGetValue(c, out var v) ? FormatValue(v) : string.Empty)));
                        await writer.WriteLineAsync(line);
                    }
                }
            }

            return (memory.ToArray(), "application/zip", tables.Count, tables.Sum(t => t.Rows.Count));
        }

        private async Task<(byte[] Content, string ContentType, int Tables, int Rows)> BuildSqlScriptAsync(
            bool includeSensitive, CancellationToken cancellationToken)
        {
            var tables = await ReadAllTablesAsync(includeSensitive, cancellationToken);
            var sb = new StringBuilder();

            sb.AppendLine("-- Scholar Dashboard data backup");
            sb.AppendLine($"-- Generated (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"-- Sensitive columns included: {includeSensitive}");
            sb.AppendLine("-- Data only. Create the schema with EF migrations before restoring.");
            sb.AppendLine();
            sb.AppendLine("BEGIN;");
            sb.AppendLine();

            foreach (var table in tables)
            {
                if (table.Rows.Count == 0) continue;

                sb.AppendLine($"-- {table.Name} ({table.Rows.Count} rows)");

                foreach (var row in table.Rows)
                {
                    var columns = string.Join(", ", table.Columns.Select(c => $"\"{c}\""));
                    var values = string.Join(", ", table.Columns.Select(c =>
                        SqlLiteral(row.TryGetValue(c, out var v) ? v : null)));

                    sb.AppendLine($"INSERT INTO \"{table.Name}\" ({columns}) VALUES ({values});");
                }

                sb.AppendLine();
            }

            sb.AppendLine("COMMIT;");

            return (Encoding.UTF8.GetBytes(sb.ToString()), "application/sql",
                tables.Count, tables.Sum(t => t.Rows.Count));
        }

        private async Task<(byte[] Content, string ContentType, int Tables, int Rows)> BuildPgDumpAsync(
            CancellationToken cancellationToken)
        {
            if (!await IsPgDumpAvailableAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "pg_dump is not available in this container. Add postgresql-client to the Dockerfile, " +
                    "or use the JSON, CSV or SQL formats which need no external tooling.");
            }

            var connectionString = _context.Database.GetConnectionString()
                ?? throw new InvalidOperationException("No connection string available.");

            var builder = new NpgsqlConnectionStringBuilder(connectionString);

            var outputPath = Path.Combine(Path.GetTempPath(), $"pgdump-{Guid.NewGuid():N}.dump");

            var startInfo = new ProcessStartInfo
            {
                FileName = "pg_dump",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Arguments passed individually rather than as one string: the password never goes
            // on the command line (it would be visible in the process list), and a database
            // name containing a space or quote cannot break out of the argument.
            startInfo.ArgumentList.Add("--host"); startInfo.ArgumentList.Add(builder.Host ?? "localhost");
            startInfo.ArgumentList.Add("--port"); startInfo.ArgumentList.Add((builder.Port).ToString());
            startInfo.ArgumentList.Add("--username"); startInfo.ArgumentList.Add(builder.Username ?? "postgres");
            startInfo.ArgumentList.Add("--dbname"); startInfo.ArgumentList.Add(builder.Database ?? "postgres");
            startInfo.ArgumentList.Add("--format"); startInfo.ArgumentList.Add("custom");
            startInfo.ArgumentList.Add("--no-owner");
            startInfo.ArgumentList.Add("--no-acl");
            startInfo.ArgumentList.Add("--file"); startInfo.ArgumentList.Add(outputPath);

            startInfo.EnvironmentVariables["PGPASSWORD"] = builder.Password ?? string.Empty;

            try
            {
                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Could not start pg_dump.");

                var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    // The most common failure is a version mismatch: pg_dump must be at least
                    // the server's major version. Surface it verbatim rather than "failed".
                    throw new InvalidOperationException(
                        $"pg_dump exited with code {process.ExitCode}: {stderr.Trim()}");
                }

                var content = await File.ReadAllBytesAsync(outputPath, cancellationToken);

                // pg_dump captures the whole database; table and row counts aren't meaningful
                // here without parsing the archive, so report them as unknown rather than wrong.
                return (content, "application/octet-stream", 0, 0);
            }
            finally
            {
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not delete temporary dump {Path}.", outputPath); }
                }
            }
        }

        // ── Data access ───────────────────────────────────────────────────────

        private sealed record TableData(string Name, List<string> Columns, List<Dictionary<string, object?>> Rows);

        /// <summary>
        /// Reads every user table via the live connection, redacting sensitive columns unless
        /// requested. Uses raw ADO rather than the EF model so tables added by future
        /// migrations are captured automatically — a backup that silently omits a new table is
        /// the worst possible outcome.
        /// </summary>
        private async Task<List<TableData>> ReadAllTablesAsync(bool includeSensitive, CancellationToken cancellationToken)
        {
            var results = new List<TableData>();
            var connection = (NpgsqlConnection)_context.Database.GetDbConnection();

            var shouldClose = connection.State != System.Data.ConnectionState.Open;
            if (shouldClose) await connection.OpenAsync(cancellationToken);

            try
            {
                var tableNames = new List<string>();

                await using (var listCommand = connection.CreateCommand())
                {
                    listCommand.CommandText = """
                        SELECT table_name
                        FROM information_schema.tables
                        WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
                        ORDER BY table_name
                        """;

                    await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                        tableNames.Add(reader.GetString(0));
                }

                foreach (var tableName in tableNames)
                {
                    // EF's own migrations-history table is rebuilt by applying migrations;
                    // restoring a stale copy of it would confuse the next deploy.
                    if (tableName.Equals("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var columns = new List<string>();
                    var rows = new List<Dictionary<string, object?>>();

                    await using var command = connection.CreateCommand();
                    // Table name comes from information_schema, not user input, and is quoted.
                    command.CommandText = $"SELECT * FROM \"{tableName}\"";

                    await using var rowReader = await command.ExecuteReaderAsync(cancellationToken);

                    for (var i = 0; i < rowReader.FieldCount; i++)
                        columns.Add(rowReader.GetName(i));

                    var redacted = !includeSensitive && SensitiveColumns.TryGetValue(tableName, out var sensitive)
                        ? new HashSet<string>(sensitive, StringComparer.OrdinalIgnoreCase)
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    while (await rowReader.ReadAsync(cancellationToken))
                    {
                        var row = new Dictionary<string, object?>(columns.Count);

                        for (var i = 0; i < rowReader.FieldCount; i++)
                        {
                            var name = columns[i];

                            if (redacted.Contains(name))
                            {
                                // Marker rather than null: a restored row keeps its NOT NULL
                                // shape, and it is obvious the value was removed on purpose.
                                row[name] = RedactedMarker;
                                continue;
                            }

                            row[name] = rowReader.IsDBNull(i) ? null : rowReader.GetValue(i);
                        }

                        rows.Add(row);
                    }

                    results.Add(new TableData(tableName, columns, rows));
                }
            }
            finally
            {
                if (shouldClose) await connection.CloseAsync();
            }

            return results;
        }

        private async Task<bool> IsPgDumpAvailableAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "pg_dump",
                    ArgumentList = { "--version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null) return false;

                await process.WaitForExitAsync(cancellationToken);
                return process.ExitCode == 0;
            }
            catch
            {
                // Binary missing entirely — the normal case on the stock aspnet image.
                return false;
            }
        }

        // ── Formatting helpers ────────────────────────────────────────────────

        private static string FormatValue(object? value) => value switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToBase64String(bytes),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var needsQuoting = value.Contains(',') || value.Contains('"')
                || value.Contains('\n') || value.Contains('\r');

            return needsQuoting ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        }

        private static string SqlLiteral(object? value) => value switch
        {
            null => "NULL",
            bool b => b ? "TRUE" : "FALSE",
            byte[] bytes => $"'\\x{Convert.ToHexString(bytes)}'",
            short or int or long or float or double or decimal =>
                ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss.ffffff}'",
            DateTimeOffset dto => $"'{dto:yyyy-MM-dd HH:mm:ss.ffffffzzz}'",
            // Doubling single quotes is what makes an apostrophe in a journal entry safe.
            _ => $"'{FormatValue(value).Replace("'", "''")}'"
        };
    }
}
