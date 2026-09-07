using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NightInjection.Core.Interfaces;
using NightInjection.Core.Models;

namespace NightInjection.Infrastructure.Storage;

public sealed partial class HistoryRepository(
    IAppPathService paths,
    ILogger<HistoryRepository> logger) : IHistoryRepository
{
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private volatile bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            paths.EnsureApplicationDirectories();
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await ExecuteAsync(connection, CompatibilitySchema, cancellationToken, transaction).ConfigureAwait(false);
            await ExecuteAsync(connection, MigrationSchema, cancellationToken, transaction).ConfigureAwait(false);

            var version = await ReadVersionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (version < 2)
            {
                await ExecuteAsync(connection, LegacyHistoryMigration, cancellationToken, transaction)
                    .ConfigureAwait(false);
                await ExecuteAsync(
                    connection,
                    "UPDATE schema_info SET version = 2 WHERE singleton = 1;",
                    cancellationToken,
                    transaction).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
            LogDatabaseReady(logger, paths.DatabasePath);
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task AddAsync(NewHistoryRecord record, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO operations
                (timestamp_utc, operation, app_id, source_file, result, details, repository, dry_run)
            VALUES
                ($timestamp, $operation, $appId, $source, $result, $details, $repository, $dryRun);
            """;
        command.Parameters.AddWithValue("$timestamp", record.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$operation", record.Operation);
        command.Parameters.AddWithValue("$appId", (object?)record.AppId ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", (object?)record.SourceFile ?? DBNull.Value);
        command.Parameters.AddWithValue("$result", record.Result);
        command.Parameters.AddWithValue("$details", (object?)record.Details ?? DBNull.Value);
        command.Parameters.AddWithValue("$repository", (object?)record.Repository ?? DBNull.Value);
        command.Parameters.AddWithValue("$dryRun", record.DryRun ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HistoryRecord>> ListAsync(
        HistoryQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        query ??= new HistoryQuery();
        var predicates = new List<string>();
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            predicates.Add(
                "(operation LIKE $search ESCAPE '\\' OR app_id LIKE $search ESCAPE '\\' OR source_file LIKE $search ESCAPE '\\' OR details LIKE $search ESCAPE '\\')");
            command.Parameters.AddWithValue("$search", $"%{EscapeLike(query.Search.Trim())}%");
        }

        if (!string.IsNullOrWhiteSpace(query.Operation) && query.Operation != "All")
        {
            predicates.Add("operation = $operation");
            command.Parameters.AddWithValue("$operation", query.Operation);
        }

        if (!string.IsNullOrWhiteSpace(query.Result) && query.Result != "All")
        {
            predicates.Add("result = $result");
            command.Parameters.AddWithValue("$result", query.Result);
        }

        command.CommandText =
            $"""
             SELECT id, timestamp_utc, operation, app_id, source_file, result,
                    details, repository, dry_run
             FROM operations
             {(predicates.Count > 0 ? "WHERE " + string.Join(" AND ", predicates) : string.Empty)}
             ORDER BY timestamp_utc DESC, id DESC
             LIMIT $limit;
             """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 5_000));

        var records = new List<HistoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var timestampValue = reader.GetString(1);
            if (!DateTimeOffset.TryParse(
                    timestampValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var timestamp))
            {
                timestamp = DateTimeOffset.UnixEpoch;
            }

            records.Add(new HistoryRecord(
                reader.GetInt64(0),
                timestamp,
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetInt64(8) != 0));
        }

        return records;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "DELETE FROM operations;", cancellationToken).ConfigureAwait(false);
    }

    private SqliteConnection CreateConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };
        return new SqliteConnection(builder.ToString());
    }

    private static async Task<int> ReadVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT version FROM schema_info WHERE singleton = 1;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    private const string CompatibilitySchema =
        """
        CREATE TABLE IF NOT EXISTS juegos (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            nombre TEXT NOT NULL,
            igdb_id INTEGER,
            steam_id INTEGER,
            descripcion TEXT,
            genero TEXT,
            anio INTEGER,
            horas_jugadas REAL DEFAULT 0,
            last_played INTEGER DEFAULT 0,
            fecha_agregado INTEGER DEFAULT (strftime('%s','now') * 1000),
            cover_url TEXT,
            art_url TEXT,
            ruta_ejecutable TEXT,
            install_path TEXT,
            install_stage TEXT,
            instalado INTEGER DEFAULT 0,
            activo INTEGER DEFAULT 1,
            logo_url TEXT,
            logo_src_url TEXT,
            cover_src_url TEXT,
            art_src_url TEXT
        );
        CREATE TABLE IF NOT EXISTS sesiones (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            juego_id INTEGER NOT NULL REFERENCES juegos(id) ON DELETE CASCADE,
            inicio INTEGER NOT NULL,
            fin INTEGER,
            duracion_s INTEGER
        );
        CREATE TABLE IF NOT EXISTS fuentes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            nexus_id TEXT NOT NULL UNIQUE,
            nombre TEXT NOT NULL,
            url TEXT NOT NULL,
            active INTEGER DEFAULT 1,
            created_at INTEGER DEFAULT (strftime('%s','now') * 1000),
            updated_at INTEGER DEFAULT (strftime('%s','now') * 1000)
        );
        CREATE TABLE IF NOT EXISTS lightning_history (
            app_id TEXT PRIMARY KEY,
            game_name TEXT,
            cover_path TEXT,
            lua_path TEXT,
            manifest_path TEXT,
            repo TEXT,
            applied_at INTEGER,
            status TEXT NOT NULL,
            error TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_last_played ON juegos(last_played DESC);
        CREATE INDEX IF NOT EXISTS idx_nombre ON juegos(nombre COLLATE NOCASE);
        """;

    private const string MigrationSchema =
        """
        CREATE TABLE IF NOT EXISTS schema_info (
            singleton INTEGER PRIMARY KEY CHECK(singleton = 1),
            version INTEGER NOT NULL
        );
        INSERT OR IGNORE INTO schema_info(singleton, version) VALUES (1, 0);
        CREATE TABLE IF NOT EXISTS operations (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp_utc TEXT NOT NULL,
            operation TEXT NOT NULL,
            app_id TEXT,
            source_file TEXT,
            result TEXT NOT NULL,
            details TEXT,
            repository TEXT,
            dry_run INTEGER NOT NULL DEFAULT 0,
            legacy_key TEXT UNIQUE
        );
        CREATE INDEX IF NOT EXISTS idx_operations_timestamp ON operations(timestamp_utc DESC);
        CREATE INDEX IF NOT EXISTS idx_operations_app_id ON operations(app_id);
        CREATE INDEX IF NOT EXISTS idx_operations_result ON operations(result);
        """;

    private const string LegacyHistoryMigration =
        """
        INSERT OR IGNORE INTO operations
            (timestamp_utc, operation, app_id, source_file, result, details, repository, dry_run, legacy_key)
        SELECT
            CASE
                WHEN applied_at IS NULL THEN strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
                ELSE strftime('%Y-%m-%dT%H:%M:%fZ', applied_at / 1000.0, 'unixepoch')
            END,
            'LegacySnapshot',
            app_id,
            COALESCE(lua_path, manifest_path),
            CASE WHEN status = 'error' THEN 'Error' ELSE 'Success' END,
            error,
            repo,
            0,
            'lightning_history:' || app_id
        FROM lightning_history;
        """;

    [LoggerMessage(LogLevel.Information, "SQLite storage ready at {Path}")]
    private static partial void LogDatabaseReady(ILogger logger, string path);
}
