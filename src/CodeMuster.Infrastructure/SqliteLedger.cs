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

    private const string FileColumns =
        "path, language, content_hash, size, mtime, first_seen, last_seen, last_commit, last_commit_at, excluded_reason, deleted_at, summary, summary_hash";

    private const string UnitColumns = "id, kind, key, fingerprint, status, fidelity, lens_hash, summary, summary_hash";

    private const string MemberColumns = "unit_id, path, symbol, member_hash, distance";

    private const string RunColumns = "started_at, head_commit, files_included, files_excluded, units_total, resolution_rate";

    private const int InListChunk = 500;

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

    public async Task<IReadOnlyList<FileRecord>> GetFilesAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {FileColumns} FROM files ORDER BY path");
        return await ReadAllAsync(command, ReadFile, cancellationToken);
    }

    public async Task UpsertFilesAsync(IReadOnlyList<FileRecord> files, CancellationToken cancellationToken)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        await using var command = CreateCommand($"""
            INSERT INTO files ({FileColumns})
            VALUES ($path, $language, $content_hash, $size, $mtime, $first_seen, $last_seen, $last_commit, $last_commit_at, $excluded_reason, $deleted_at, $summary, $summary_hash)
            ON CONFLICT(path) DO UPDATE SET
                language = excluded.language, content_hash = excluded.content_hash, size = excluded.size, mtime = excluded.mtime,
                first_seen = excluded.first_seen, last_seen = excluded.last_seen, last_commit = excluded.last_commit,
                last_commit_at = excluded.last_commit_at, excluded_reason = excluded.excluded_reason, deleted_at = excluded.deleted_at,
                summary = excluded.summary, summary_hash = excluded.summary_hash
            """);
        foreach (var file in files)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$path", file.Path);
            command.Parameters.AddWithValue("$language", file.Language);
            command.Parameters.AddWithValue("$content_hash", file.ContentHash);
            command.Parameters.AddWithValue("$size", file.Size);
            command.Parameters.AddWithValue("$mtime", file.Mtime);
            command.Parameters.AddWithValue("$first_seen", file.FirstSeen);
            command.Parameters.AddWithValue("$last_seen", file.LastSeen);
            command.Parameters.AddWithValue("$last_commit", Db(file.LastCommit));
            command.Parameters.AddWithValue("$last_commit_at", Db(file.LastCommitAt));
            command.Parameters.AddWithValue("$excluded_reason", Db(file.ExcludedReason));
            command.Parameters.AddWithValue("$deleted_at", Db(file.DeletedAt));
            command.Parameters.AddWithValue("$summary", Db(file.Summary));
            command.Parameters.AddWithValue("$summary_hash", Db(file.SummaryHash));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Unit>> GetUnitsAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {UnitColumns} FROM units ORDER BY seq");
        return await ReadAllAsync(command, ReadUnit, cancellationToken);
    }

    public async Task<Unit?> GetUnitAsync(string unitId, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {UnitColumns} FROM units WHERE id = $id");
        command.Parameters.AddWithValue("$id", unitId);
        return (await ReadAllAsync(command, ReadUnit, cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<UnitMember>> GetMembersAsync(IReadOnlyList<string> unitIds, CancellationToken cancellationToken)
    {
        var members = new List<UnitMember>();
        foreach (var chunk in unitIds.Chunk(InListChunk))
        {
            var names = chunk.Select((_, i) => "$id" + i.ToString(CultureInfo.InvariantCulture)).ToArray();
            await using var command = CreateCommand($"SELECT {MemberColumns} FROM unit_members WHERE unit_id IN ({string.Join(", ", names)}) ORDER BY rowid");
            for (var i = 0; i < chunk.Length; i++)
            {
                command.Parameters.AddWithValue(names[i], chunk[i]);
            }

            members.AddRange(await ReadAllAsync(command, ReadMember, cancellationToken));
        }

        return members;
    }

    public async Task UpsertUnitsAsync(IReadOnlyList<Unit> units, IReadOnlyList<UnitMember> members, CancellationToken cancellationToken)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        await using var upsert = CreateCommand($"""
            INSERT INTO units (seq, {UnitColumns})
            VALUES ((SELECT COALESCE(MAX(seq), 0) + 1 FROM units), $id, $kind, $key, $fingerprint, $status, $fidelity, $lens_hash, $summary, $summary_hash)
            ON CONFLICT(id) DO UPDATE SET
                kind = excluded.kind, key = excluded.key, fingerprint = excluded.fingerprint, status = excluded.status,
                fidelity = excluded.fidelity, lens_hash = excluded.lens_hash, summary = excluded.summary, summary_hash = excluded.summary_hash
            """);
        await using var clear = CreateCommand("DELETE FROM unit_members WHERE unit_id = $unit_id");
        foreach (var unit in units)
        {
            upsert.Parameters.Clear();
            upsert.Parameters.AddWithValue("$id", unit.Id);
            upsert.Parameters.AddWithValue("$kind", Name(unit.Kind));
            upsert.Parameters.AddWithValue("$key", unit.Key);
            upsert.Parameters.AddWithValue("$fingerprint", unit.Fingerprint);
            upsert.Parameters.AddWithValue("$status", Name(unit.Status));
            upsert.Parameters.AddWithValue("$fidelity", Name(unit.Fidelity));
            upsert.Parameters.AddWithValue("$lens_hash", Db(unit.LensHash));
            upsert.Parameters.AddWithValue("$summary", Db(unit.Summary));
            upsert.Parameters.AddWithValue("$summary_hash", Db(unit.SummaryHash));
            await upsert.ExecuteNonQueryAsync(cancellationToken);

            clear.Parameters.Clear();
            clear.Parameters.AddWithValue("$unit_id", unit.Id);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insert = CreateCommand($"INSERT INTO unit_members ({MemberColumns}) VALUES ($unit_id, $path, $symbol, $member_hash, $distance)");
        foreach (var member in members)
        {
            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("$unit_id", member.UnitId);
            insert.Parameters.AddWithValue("$path", member.Path);
            insert.Parameters.AddWithValue("$symbol", Db(member.Symbol));
            insert.Parameters.AddWithValue("$member_hash", member.MemberHash);
            insert.Parameters.AddWithValue("$distance", member.Distance);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Unit>> NextAsync(int batch, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {UnitColumns} FROM units WHERE status IN ($pending, $stale, $failed) ORDER BY seq LIMIT $batch");
        command.Parameters.AddWithValue("$pending", Name(UnitStatus.Pending));
        command.Parameters.AddWithValue("$stale", Name(UnitStatus.Stale));
        command.Parameters.AddWithValue("$failed", Name(UnitStatus.Failed));
        command.Parameters.AddWithValue("$batch", batch);
        return await ReadAllAsync(command, ReadUnit, cancellationToken);
    }

    public async Task RecordAnalysisAsync(Analysis analysis, IReadOnlyList<Finding> findings, CancellationToken cancellationToken)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        await using var insertAnalysis = CreateCommand("""
            INSERT INTO analyses (unit_id, fingerprint, lens_hash, created_at, succeeded, summary, error)
            VALUES ($unit_id, $fingerprint, $lens_hash, $created_at, $succeeded, $summary, $error)
            RETURNING id
            """);
        insertAnalysis.Parameters.AddWithValue("$unit_id", analysis.UnitId);
        insertAnalysis.Parameters.AddWithValue("$fingerprint", analysis.Fingerprint);
        insertAnalysis.Parameters.AddWithValue("$lens_hash", analysis.LensHash);
        insertAnalysis.Parameters.AddWithValue("$created_at", analysis.CreatedAt);
        insertAnalysis.Parameters.AddWithValue("$succeeded", analysis.Succeeded);
        insertAnalysis.Parameters.AddWithValue("$summary", Db(analysis.Summary));
        insertAnalysis.Parameters.AddWithValue("$error", Db(analysis.Error));
        var analysisId = Convert.ToInt64(await insertAnalysis.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

        await using var insertFinding = CreateCommand("""
            INSERT INTO findings (analysis_id, path, line_start, line_end, severity, category, claim, evidence, confidence, lens_id)
            VALUES ($analysis_id, $path, $line_start, $line_end, $severity, $category, $claim, $evidence, $confidence, $lens_id)
            """);
        foreach (var finding in findings)
        {
            insertFinding.Parameters.Clear();
            insertFinding.Parameters.AddWithValue("$analysis_id", analysisId);
            insertFinding.Parameters.AddWithValue("$path", finding.Path);
            insertFinding.Parameters.AddWithValue("$line_start", finding.LineStart);
            insertFinding.Parameters.AddWithValue("$line_end", finding.LineEnd);
            insertFinding.Parameters.AddWithValue("$severity", Name(finding.Severity));
            insertFinding.Parameters.AddWithValue("$category", finding.Category);
            insertFinding.Parameters.AddWithValue("$claim", finding.Claim);
            insertFinding.Parameters.AddWithValue("$evidence", finding.Evidence);
            insertFinding.Parameters.AddWithValue("$confidence", finding.Confidence);
            insertFinding.Parameters.AddWithValue("$lens_id", finding.LensId);
            await insertFinding.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var update = CreateCommand(analysis.Succeeded
            ? "UPDATE units SET status = $status, summary = $summary, summary_hash = $summary_hash, lens_hash = $lens_hash WHERE id = $unit_id"
            : "UPDATE units SET status = $status WHERE id = $unit_id");
        update.Parameters.AddWithValue("$unit_id", analysis.UnitId);
        update.Parameters.AddWithValue("$status", Name(analysis.Succeeded ? UnitStatus.Done : UnitStatus.Failed));
        update.Parameters.AddWithValue("$summary", Db(analysis.Summary));
        update.Parameters.AddWithValue("$summary_hash", analysis.Fingerprint);
        update.Parameters.AddWithValue("$lens_hash", analysis.LensHash);
        await update.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordRunAsync(Run run, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"INSERT INTO runs ({RunColumns}) VALUES ($started_at, $head_commit, $files_included, $files_excluded, $units_total, $resolution_rate)");
        command.Parameters.AddWithValue("$started_at", run.StartedAt);
        command.Parameters.AddWithValue("$head_commit", run.HeadCommit);
        command.Parameters.AddWithValue("$files_included", run.FilesIncluded);
        command.Parameters.AddWithValue("$files_excluded", run.FilesExcluded);
        command.Parameters.AddWithValue("$units_total", run.UnitsTotal);
        command.Parameters.AddWithValue("$resolution_rate", Db(run.ResolutionRate));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Run?> GetLastRunAsync(CancellationToken cancellationToken)
    {
        await using var command = CreateCommand($"SELECT {RunColumns} FROM runs ORDER BY id DESC LIMIT 1");
        return (await ReadAllAsync(command, ReadRun, cancellationToken)).SingleOrDefault();
    }

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

    private static async Task<IReadOnlyList<T>> ReadAllAsync<T>(SqliteCommand command, Func<SqliteDataReader, T> read, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    private static FileRecord ReadFile(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
        Text(reader, 7), Text(reader, 8), Text(reader, 9), Text(reader, 10), Text(reader, 11), Text(reader, 12));

    private static Unit ReadUnit(SqliteDataReader reader) => new(
        reader.GetString(0), Enum.Parse<UnitKind>(reader.GetString(1), ignoreCase: true), reader.GetString(2), reader.GetString(3),
        Enum.Parse<UnitStatus>(reader.GetString(4), ignoreCase: true), Enum.Parse<Fidelity>(reader.GetString(5), ignoreCase: true),
        Text(reader, 6), Text(reader, 7), Text(reader, 8));

    private static UnitMember ReadMember(SqliteDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), Text(reader, 2), reader.GetString(3), reader.GetInt32(4));

    private static Run ReadRun(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4),
        reader.IsDBNull(5) ? null : reader.GetDouble(5));

    private static string? Text(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static object Db(object? value) => value ?? DBNull.Value;
}
