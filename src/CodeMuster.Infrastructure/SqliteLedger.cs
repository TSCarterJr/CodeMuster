using System.Globalization;
using CodeMuster.Domain;
using Microsoft.Data.Sqlite;

namespace CodeMuster.Infrastructure;

public sealed class SqliteLedger : ILedger, IDisposable
{
    private const string Schema = """
        CREATE TABLE files (
            path TEXT PRIMARY KEY,
            language TEXT NOT NULL,
            content_hash TEXT NOT NULL,
            size INTEGER NOT NULL,
            mtime TEXT NOT NULL,
            first_seen TEXT NOT NULL,
            last_seen TEXT NOT NULL,
            last_commit TEXT,
            last_commit_at TEXT,
            excluded_reason TEXT,
            deleted_at TEXT,
            summary TEXT,
            summary_hash TEXT);
        CREATE TABLE units (
            id TEXT PRIMARY KEY,
            seq INTEGER NOT NULL,
            kind TEXT NOT NULL,
            key TEXT NOT NULL,
            fingerprint TEXT NOT NULL,
            status TEXT NOT NULL,
            fidelity TEXT NOT NULL,
            lens_hash TEXT,
            summary TEXT,
            summary_hash TEXT);
        CREATE INDEX units_seq ON units (seq);
        CREATE TABLE unit_members (
            unit_id TEXT NOT NULL,
            path TEXT NOT NULL,
            symbol TEXT,
            member_hash TEXT NOT NULL,
            distance INTEGER NOT NULL);
        CREATE INDEX unit_members_unit_id ON unit_members (unit_id);
        CREATE TABLE runs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            started_at TEXT NOT NULL,
            head_commit TEXT NOT NULL,
            files_included INTEGER NOT NULL,
            files_excluded INTEGER NOT NULL,
            units_total INTEGER NOT NULL,
            resolution_rate REAL);
        CREATE TABLE analyses (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            unit_id TEXT NOT NULL,
            fingerprint TEXT NOT NULL,
            lens_hash TEXT NOT NULL,
            created_at TEXT NOT NULL,
            succeeded INTEGER NOT NULL,
            summary TEXT,
            error TEXT);
        CREATE TABLE findings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            analysis_id INTEGER NOT NULL,
            path TEXT NOT NULL,
            line_start INTEGER NOT NULL,
            line_end INTEGER NOT NULL,
            severity TEXT NOT NULL,
            category TEXT NOT NULL,
            claim TEXT NOT NULL,
            evidence TEXT NOT NULL,
            confidence REAL NOT NULL,
            lens_id TEXT NOT NULL);
        """;

    private readonly SqliteConnection _connection;

    private SqliteLedger(SqliteConnection connection)
    {
        _connection = connection;
    }

    public static async Task<SqliteLedger> OpenAsync(string databasePath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            var ledger = new SqliteLedger(connection);
            await ledger.ExecuteAsync("PRAGMA busy_timeout = 5000", cancellationToken);
            await ledger.ExecuteAsync("PRAGMA journal_mode = WAL", cancellationToken);
            await ledger.MigrateAsync(cancellationToken);
            return ledger;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    internal async Task<long> ReadPragmaAsync(string name, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand("PRAGMA " + name);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public Task<IReadOnlyList<FileRecord>> GetFilesAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task UpsertFilesAsync(IReadOnlyList<FileRecord> files, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<Unit>> GetUnitsAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<Unit?> GetUnitAsync(string unitId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<UnitMember>> GetMembersAsync(IReadOnlyList<string> unitIds, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task UpsertUnitsAsync(IReadOnlyList<Unit> units, IReadOnlyList<UnitMember> members, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<Unit>> NextAsync(int batch, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task RecordAnalysisAsync(Analysis analysis, IReadOnlyList<Finding> findings, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task RecordRunAsync(Run run, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<Run?> GetLastRunAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        if (await ReadPragmaAsync("user_version", cancellationToken) < 1)
        {
            await ExecuteAsync(Schema, cancellationToken);
            await ExecuteAsync("PRAGMA user_version = 1", cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteCommand CreateCommand(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }
}
